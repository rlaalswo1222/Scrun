from logging.config import fileConfig

from sqlalchemy import engine_from_config
from sqlalchemy import pool

from alembic import context

# this is the Alembic Config object, which provides
# access to the values within the .ini file in use.
config = context.config

# Interpret the config file for Python logging.
# This line sets up loggers basically.
# 명령줄 alembic일 때만 alembic.ini 로그 설정을 쓴다. 코드에서 부를 때(테스트·예시 내보내기, app/db/testing.py)는
# configure_logger=False → 루트 로거의 처리기(pytest 로그 캡처 등)를 갈아 끼우지 않음.
# disable_existing_loggers=False: 이미 만든 로거(scrun.* 등)를 끄지 않게(기본값 True면 마이그레이션 뒤 로그가 사라짐)
if config.config_file_name is not None and config.attributes.get("configure_logger", True):
    fileConfig(config.config_file_name, disable_existing_loggers=False)

# 우리 테이블(app/db/models.py)과 접속 주소(환경 변수 DATABASE_URL, 없으면 backend/.env).
# DB 주소만 읽는다(app.core.config의 JWT_SECRET·카카오 설정 검사 없이 마이그레이션이 돌게)
import os

from app.core.envfile import database_url
from app.db.models import Base

target_metadata = Base.metadata
config.set_main_option("sqlalchemy.url", os.environ.get("ALEMBIC_DATABASE_URL") or database_url())

# other values from the config, defined by the needs of env.py,
# can be acquired:
# my_important_option = config.get_main_option("my_important_option")
# ... etc.


def run_migrations_offline() -> None:
    """Run migrations in 'offline' mode.

    This configures the context with just a URL
    and not an Engine, though an Engine is acceptable
    here as well.  By skipping the Engine creation
    we don't even need a DBAPI to be available.

    Calls to context.execute() here emit the given string to the
    script output.

    """
    url = config.get_main_option("sqlalchemy.url")
    context.configure(
        url=url,
        target_metadata=target_metadata,
        literal_binds=True,
        dialect_opts={"paramstyle": "named"},
    )

    with context.begin_transaction():
        context.run_migrations()


def run_migrations_online() -> None:
    """Run migrations in 'online' mode.

    In this scenario we need to create an Engine
    and associate a connection with the context.

    """
    connectable = engine_from_config(
        config.get_section(config.config_ini_section, {}),
        prefix="sqlalchemy.",
        poolclass=pool.NullPool,
    )

    with connectable.connect() as connection:
        context.configure(
            connection=connection, target_metadata=target_metadata
        )

        with context.begin_transaction():
            context.run_migrations()


if context.is_offline_mode():
    run_migrations_offline()
else:
    run_migrations_online()
