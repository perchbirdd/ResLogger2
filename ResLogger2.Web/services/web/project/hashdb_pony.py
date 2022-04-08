import time
from typing import List
from pony.flask import Pony
from pony.orm import *

from .index_parse_result import IndexParseResult
from .util import HashResult

db = Database()


class Path(db.Entity):
    index = Required(int, index=True)
    folder_hash = Required(int, index=True)
    file_hash = Required(int, index=True)
    full_hash = Required(int, index=True)
    path = Optional(str)
    composite_key(index, folder_hash, file_hash, full_hash)

    # def __init__(self, index: int, folder_hash: int, file_hash: int, full_hash: int):
    #     self.index = index
    #     self.full_hash = full_hash
    #     self.folder_hash = folder_hash
    #     self.file_hash = file_hash


class Index1Path(db.Entity):
    index = Required(int, index=True)
    folder_hash = Required(int, index=True)
    file_hash = Required(int, index=True)
    composite_key(index, folder_hash, file_hash)

    # def __init__(self, index: int, folder_hash: int, file_hash: int):
    #     self.index = index
    #     self.folder_hash = folder_hash
    #     self.file_hash = file_hash


class Index2Path(db.Entity):
    index = Required(int, index=True)
    full_hash = Required(int, index=True)
    composite_key(index, full_hash)

    # def __init__(self, index: int, full_hash: int):
    #     self.index = index
    #     self.full_hash = full_hash


class HashDatabasePony:
    def __init__(self, app):
        Pony(app)
        db.bind(app.config["PONY"])
        db.generate_mapping()

    def process_index(self, index: IndexParseResult):
        start = time.perf_counter()

        with db_session:
            for element in index.hashes.values():
                if element.full is None and element.file is not None:
                    Index1Path(index=index.index_id,
                               folder_hash=element.folder,
                               file_hash=element.file)
                if element.file is None and element.full is not None:
                    Index2Path(index=index.index_id,
                               full_hash=element.full)
                if element.file is not None and element.full is not None:
                    Path(index=index.index_id,
                         folder_hash=element.folder,
                         file_hash=element.file,
                         full_hash=element.full)

        stop = time.perf_counter()
        t = stop - start
        print(f"took {t * 1000}ms")

    def handle_paths(self, paths: List[HashResult]) -> None:
        pass

    def handle_path(self, path: HashResult) -> None:
        pass

    def create(self):
        # db.drop_all_tables()
        db.create_tables()

    @staticmethod
    def commit():
        db.session.commit()
