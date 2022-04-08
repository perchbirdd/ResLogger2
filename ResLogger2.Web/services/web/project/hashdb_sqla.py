import time
from flask_sqlalchemy import SQLAlchemy
from typing import List

from sqlalchemy.dialects.postgresql import insert
from sqlalchemy.exc import IntegrityError

from .index_parse_result import IndexParseResult
from .util import HashResult

db = SQLAlchemy()


class Path(db.Model):
    __table_args__ = (
        db.UniqueConstraint("file_hash", "folder_hash", "full_hash", "index", name="unique_path"),
    )
    query: db.Query
    id = db.Column(db.Integer, primary_key=True, autoincrement=True)
    index = db.Column(db.Integer, index=True, nullable=False)
    folder_hash = db.Column(db.Integer, index=True)
    file_hash = db.Column(db.Integer, index=True)
    full_hash = db.Column(db.Integer, index=True)
    path = db.Column(db.Text, nullable=True)

    def __init__(self, index: int, folder_hash: int, file_hash: int, full_hash: int):
        self.index = index
        self.full_hash = full_hash
        self.folder_hash = folder_hash
        self.file_hash = file_hash


class HashDatabaseSQLA:
    def __init__(self, app):
        db.init_app(app)

    def process_index(self, index: IndexParseResult):
        start = time.perf_counter()
        # import logging
        # logging.basicConfig()
        # logging.getLogger('sqlalchemy.engine').setLevel(logging.INFO)
        index1s = []
        index2s = []
        paths = []

        for element in index.hashes.values():
            p = {"index": index.index_id,
                 "folder_hash": element.folder,
                 "file_hash": element.file,
                 "full_hash": element.full}
            paths.append(p)

        if len(paths) > 0:
            db.session.execute(insert(Path).values(paths).on_conflict_do_update(
                constraint="unique_paths",

            ))
        db.session.commit()

        stop = time.perf_counter()
        t = stop - start
        print(f"took {t * 1000}ms")

    def handle_paths(self, paths: List[HashResult]) -> None:
        pass

    def handle_path(self, path: HashResult) -> None:
        pass

    def create(self):
        db.drop_all()
        db.create_all()
        db.session.commit()

    @staticmethod
    def commit():
        db.session.commit()
