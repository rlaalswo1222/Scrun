"""YOLOX는 형 변환을 `x.type(y.type())`처럼 글자('torch.cuda.FloatTensor')로 한다. 맥 GPU(MPS)의 'torch.mps.FloatTensor'는
PyTorch가 이 글자를 못 받아서 멈춘다 → 학습·채점할 때만 `Tensor.type`이 이 글자를 같은 장치·형으로 바꾸게 한다.
설치된 YOLOX는 고치지 않는다."""
import torch

_DTYPES = {"torch.mps.FloatTensor": torch.float32, "torch.mps.HalfTensor": torch.float16,
           "torch.mps.LongTensor": torch.int64, "torch.mps.IntTensor": torch.int32, "torch.mps.BoolTensor": torch.bool}
_orig_type = torch.Tensor.type


def _type(self, dtype=None, non_blocking=False, **kwargs):
    if isinstance(dtype, str) and dtype in _DTYPES:
        return self.to(device="mps", dtype=_DTYPES[dtype], non_blocking=non_blocking)
    if dtype is None:
        return _orig_type(self)
    return _orig_type(self, dtype, non_blocking, **kwargs)


torch.Tensor.type = _type
