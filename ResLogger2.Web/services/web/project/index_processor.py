import glob
import os
import struct
from typing import Iterable

from .index_parse_result import IndexParseResult, IndexElement
from .util import get_gamever_from_path


class IndexProcessor:
    indexes = {}

    def __init__(self, path: str):
        self.path = path
        print(f"repository init: {path}")

    def read(self) -> Iterable[IndexParseResult]:

        # Get all files, index or index2, in the given path
        index_path_list = glob.glob(f"{self.path}/**/*.index*", recursive=True)

        # We will assume that index and index2 both exist for all of these paths
        # So we strip the 2 from index2 and remove duplicates
        index_path_set = set()
        for element in index_path_list:
            add_element = element
            if element.endswith("2"):
                add_element = element[:len(element) - 1]
            index_path_set.add(add_element)

        # Force some semblance of consistency
        index_path_sorted = sorted(index_path_set)
        for index_path in index_path_sorted:
            # We don't care what index file we got, we should assume it exists
            result = self.read_index(index_path)
            result = self.read_index(f"{index_path}2", result)
            result.postprocess()
            yield result

    def read_index(self, index_path: str, parse_progress: IndexParseResult = None) -> IndexParseResult:
        index_id_str = os.path.splitext(os.path.basename(index_path))[0].replace(".win32", "")
        index_id = int(index_id_str, 16)

        if not os.path.exists(index_path):
            return parse_progress

        with open(index_path, "rb") as index:
            magic = index.read(6)

            for i in range(0, len(magic)):
                if magic[i] != bytes("SqPack", encoding="utf-8")[i]:
                    print(f"invalid index: {index_path}")
                    return parse_progress

            index.seek(8)
            plat = struct.unpack("b", index.read(1))
            index.seek(12)
            endian = plat[0]
            if endian != 0:
                return parse_progress
            endian_id = ">" if endian == 1 else "<"
            skip_size = struct.unpack(f"{endian_id}I", index.read(4))[0]

            index.seek(1324)
            index_type = struct.unpack(f"{endian_id}I", index.read(4))[0]

            index.seek(skip_size + 8)
            seek_to = struct.unpack(f"{endian_id}I", index.read(4))[0]
            data_size = struct.unpack(f"{endian_id}I", index.read(4))[0]
            if index_type == 0:
                hash_count = data_size // 16
            else:
                hash_count = data_size // 8

            index.seek(seek_to)

            gamever = get_gamever_from_path(index_path)
            if parse_progress is not None:
                result = parse_progress
            else:
                result = IndexParseResult(gamever=gamever, index_id=index_id)
            if index_type == 0:
                for i in range(0, hash_count):
                    file = struct.unpack(f"{endian_id}i", index.read(4))[0]
                    folder = struct.unpack(f"{endian_id}i", index.read(4))[0]
                    offset = struct.unpack(f"{endian_id}i", index.read(4))[0]

                    if offset in result.hashes:
                        existing = result.hashes[offset]
                        existing.file = file
                        existing.folder = folder
                    else:
                        result.hashes[offset] = IndexElement(file=file, folder=folder)
                    index.seek(index.tell() + 4)
            elif index_type == 2:
                for i in range(0, hash_count):
                    full = struct.unpack(f"{endian_id}i", index.read(4))[0]
                    offset = struct.unpack(f"{endian_id}i", index.read(4))[0]
                    if offset in result.hashes:
                        existing = result.hashes[offset]
                        existing.full = full
                    else:
                        result.hashes[offset] = IndexElement(full=full)

            return result
