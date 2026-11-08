"""만료된 refresh 토큰 줄 전부 삭제. 운영 서버에서 하루 한 번 돌린다(cron 등, 배포 BL-51).

실행: backend 폴더에서 `python -m scripts.purge_refresh_tokens` (.env의 DATABASE_URL = 지금 서버의 DB)
로그인할 때도 그 사용자의 만료 줄은 지우지만(app/core/sessions.py), 다시 로그인하지 않는 사용자의 줄은 이걸로 지운다.
"""
from app.core.sessions import purge_expired
from app.db.session import SessionLocal


def main() -> None:
    with SessionLocal() as db:
        print(f"만료된 refresh 토큰 {purge_expired(db)}줄 삭제")


if __name__ == "__main__":
    main()
