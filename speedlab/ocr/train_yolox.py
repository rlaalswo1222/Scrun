"""YOLOX-Nano·Tiny 학습(ADR-0012). YOLOX의 모델·손실만 쓰고 학습 루프는 여기(YOLOX Trainer는 CUDA 전용) → 맥 GPU(MPS).

사용: python train_yolox.py --size nano --epochs 60 --out runs/yolox_nano
데이터: data/public/7seg_v3/{train,valid,test}(공개), data/synth/{train,val}(synth7seg로 만든 것)
끝나면 runs/.../best.pth와 채점(eval.py와 같은 글자 정확도)을 남긴다.
"""
import argparse
import json
import math
import time
from pathlib import Path

import numpy as np
import torch
import mps_patch  # noqa: F401  (YOLOX 머리를 MPS에서 돌게)
from yolox.models import YOLOPAFPN, YOLOX, YOLOXHead
from yolox.utils import postprocess

from dataset import IN_H, IN_W, DigitDataset, load_coco
from readout import read_text
from synth7seg import CLASSES

SIZES = {"nano": (0.33, 0.25, True), "tiny": (0.33, 0.375, False)}  # depth, width, depthwise(YOLOX exps/default)
HERE = Path(__file__).parent


def build(size: str) -> YOLOX:
    depth, width, dw = SIZES[size]
    in_ch = [256, 512, 1024]
    model = YOLOX(YOLOPAFPN(depth, width, in_channels=in_ch, depthwise=dw),
                  YOLOXHead(len(CLASSES), width, in_channels=in_ch, depthwise=dw))
    for m in model.modules():  # YOLOX exp 기본값
        if isinstance(m, torch.nn.BatchNorm2d):
            m.eps, m.momentum = 1e-3, 0.03
    model.head.initialize_biases(1e-2)
    return model


def datasets():
    pub = HERE / "data/public/7seg_v3"
    syn = HERE / "data/synth"
    train = load_coco(pub / "train") + load_coco(syn / "train")
    val = {"public_valid": load_coco(pub / "valid"), "synth_val": load_coco(syn / "val")}
    return train, val


@torch.no_grad()
def predict(model, imgs: torch.Tensor, device, conf=0.01, nms=0.5):
    model.eval()
    out = model(imgs.to(device))
    dets = postprocess(out.float().cpu(), len(CLASSES), conf, nms, class_agnostic=True)
    res = []
    for d in dets:
        if d is None:
            res.append(np.zeros((0, 6), np.float32))
        else:
            d = d.numpy()
            res.append(np.concatenate([d[:, :4], (d[:, 4] * d[:, 5])[:, None], d[:, 6:7]], 1))
    return res


def text_accuracy(model, items, device, bs=64) -> tuple[float, list]:
    ds = DigitDataset(items, train=False)
    wrong, right = [], 0
    for i in range(0, len(ds), bs):
        batch = [ds[k][0] for k in range(i, min(i + bs, len(ds)))]
        for k, dets in enumerate(predict(model, torch.stack(batch), device)):
            truth = items[i + k][2]
            got = read_text(dets)
            if got == truth:
                right += 1
            else:
                wrong.append((str(items[i + k][0].name), truth, got))
    return right / max(1, len(items)), wrong


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--size", default="nano", choices=list(SIZES))
    ap.add_argument("--epochs", type=int, default=60)
    ap.add_argument("--bs", type=int, default=64)
    ap.add_argument("--lr", type=float, default=0.01)
    ap.add_argument("--out", default="runs/yolox_nano")
    ap.add_argument("--device", default="mps" if torch.backends.mps.is_available() else "cpu")
    ap.add_argument("--limit", type=int, default=0, help="빠른 확인용: 학습 장 수 제한")
    args = ap.parse_args()
    out = HERE / args.out
    out.mkdir(parents=True, exist_ok=True)
    torch.manual_seed(0)

    train_items, val_sets = datasets()
    if args.limit:
        train_items = train_items[: args.limit]
    bs = min(args.bs, len(train_items))   # --limit이 배치보다 작아도 배치가 0개가 되지 않게(#61 리뷰)
    loader = torch.utils.data.DataLoader(DigitDataset(train_items, train=True), batch_size=bs, shuffle=True,
                                         num_workers=4, drop_last=True, persistent_workers=True)
    device = torch.device(args.device)
    model = build(args.size).to(device)
    decay, no_decay = [], []
    for n, p in model.named_parameters():
        (no_decay if p.ndim <= 1 else decay).append(p)
    opt = torch.optim.SGD([{"params": decay, "weight_decay": 5e-4}, {"params": no_decay, "weight_decay": 0}],
                          lr=args.lr, momentum=0.9, nesterov=True)
    iters = args.epochs * len(loader)
    warm = min(5 * len(loader), iters // 10)
    log = {"args": vars(args), "train_images": len(train_items), "epochs": []}
    best, step, t0 = -1.0, 0, time.time()
    lr = args.lr
    for ep in range(args.epochs):
        model.train()
        losses = []
        for imgs, labels in loader:
            lr = args.lr * (step / warm) ** 2 if step < warm else \
                0.05 * args.lr + 0.95 * args.lr * 0.5 * (1 + math.cos(math.pi * (step - warm) / max(1, iters - warm)))
            for g in opt.param_groups:
                g["lr"] = lr
            loss = model(imgs.to(device), labels.to(device))["total_loss"]
            opt.zero_grad()
            loss.backward()
            opt.step()
            losses.append(float(loss))
            step += 1
        row = {"epoch": ep + 1, "loss": float(np.mean(losses)), "lr": lr, "min": round((time.time() - t0) / 60, 1)}
        if (ep + 1) % 5 == 0 or ep + 1 == args.epochs:
            for name, items in val_sets.items():
                row[name], _ = text_accuracy(model, items, device)
            score = row["public_valid"] + row["synth_val"]
            if score > best:
                best = score
                torch.save(model.state_dict(), out / "best.pth")
        torch.save(model.state_dict(), out / "last.pth")
        log["epochs"].append(row)
        print(json.dumps(row, ensure_ascii=False), flush=True)
        (out / "log.json").write_text(json.dumps(log, ensure_ascii=False, indent=1))


if __name__ == "__main__":
    main()
