# 공용 개발 서버(오라클 ARM)용 API 이미지. 서버에서 직접 빌드한다(deploy/docker-compose.server.yml).
# 로컬 개발은 이 이미지 없이 uvicorn으로 돌린다(README).
FROM python:3.11-slim

ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1
WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY alembic.ini .
COPY app app
COPY migrations migrations
COPY scripts scripts

RUN useradd --uid 1000 --create-home app
USER app

# 뜰 때마다 마이그레이션을 끝까지 올린 뒤 API를 연다. Caddy 뒤에 있으므로 프록시 헤더를 믿는다.
CMD ["sh", "-c", "python -m alembic upgrade head && exec uvicorn app.main:app --host 0.0.0.0 --port 8000 --proxy-headers --forwarded-allow-ips='*'"]
