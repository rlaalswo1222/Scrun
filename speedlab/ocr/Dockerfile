# 7세그먼트 OCR 비교용: ssocr(7세그먼트 전용) + Tesseract(7세그먼트 학습 파일 ssd·7seg)
FROM debian:bookworm-slim
RUN apt-get update && apt-get install -y --no-install-recommends ssocr tesseract-ocr imagemagick curl ca-certificates \
 && rm -rf /var/lib/apt/lists/*
# https://github.com/Shreeshrii/tessdata_ssd (합성 7세그먼트 글꼴로 학습한 시험판)
RUN cd /usr/share/tesseract-ocr/5/tessdata \
 && curl -fsSL -o ssd.traineddata https://github.com/Shreeshrii/tessdata_ssd/raw/master/ssd.traineddata \
 && curl -fsSL -o 7seg.traineddata https://github.com/Shreeshrii/tessdata_ssd/raw/master/7seg.traineddata \
 && ls -la
WORKDIR /w
