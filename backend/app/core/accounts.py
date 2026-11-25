"""계정 삭제(계획 0058, BL-48). 앱의 DELETE /users/me와 카카오 연결 끊기 웹훅이 같이 쓴다.

- users 줄을 지우면 딸린 행(로그인·약관·보정·기록·인증·성장·친구·같이 달린 줄 등)은 ON DELETE CASCADE로 같이 지워진다(계획 0015).
  친구 쪽 기록의 "함께 달린 러너" 줄도 같이 지워짐(정할 것 4 가)
- 실시간 방에서 바로 빼고 다시 붙지 못하게 한다. 남의 러닝에 모인 나는 저장 때 걸러짐(runs._save_companions)
- 이미 발급된 액세스 토큰은 사용자 줄이 없으면 401(security._user)이라 따로 폐기할 것이 없다
"""
import logging
from typing import Optional

from sqlalchemy import delete, select
from sqlalchemy.orm import Session

from ..db.models import AuthIdentity, User
from ..realtime import link

log = logging.getLogger("scrun.accounts")
KAKAO = "KAKAO"


def kakao_id_of(db: Session, user_id: str) -> Optional[str]:
    return db.scalar(select(AuthIdentity.provider_user_id)
                     .where(AuthIdentity.user_id == user_id, AuthIdentity.provider == KAKAO))


def user_of_kakao(db: Session, kakao_user_id: str) -> Optional[str]:
    return db.scalar(select(AuthIdentity.user_id)
                     .where(AuthIdentity.provider == KAKAO, AuthIdentity.provider_user_id == kakao_user_id))


def delete_user(db: Session, user_id: str, reason: str) -> bool:
    """사용자와 딸린 데이터를 지우고 커밋. 이미 없으면 False. 카카오 연결 끊기는 부른 쪽이 먼저(실패하면 부르지 않음)."""
    gone = db.execute(delete(User).where(User.id == user_id)).rowcount
    db.commit()
    link.hub_link.forget_user(user_id)
    if gone:
        log.info("계정 삭제: %s (%s)", user_id, reason)
    return bool(gone)
