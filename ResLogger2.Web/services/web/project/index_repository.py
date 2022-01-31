import glob
import os
import struct

from .exists_result import ExistsResult
from .util import crc32


class IndexRepository:

    indexes = {}

    def __init__(self, path: str):
        self.path = path
        print(f"x repository init: {path}")

        for path in glob.glob("**/*.index2", recursive=True):
            print(path)
            self.read_index(path)

        print(f"loaded {len(self.indexes)} index files")

    def read_index(self, index_path: str) -> None:
        index_id_str = os.path.splitext(os.path.basename(index_path))[0].replace(".win32", "")
        index_id = int(index_id_str, 16)
        print(f"reading index2: {index_path} id: {index_id} {index_id:06x}")

        with open(index_path, "rb") as index:
            magic = index.read(6)

            for i in range(0, len(magic)):
                if magic[i] != bytes("SqPack", encoding="utf-8")[i]:
                    print(f"invalid index2: {index_path}")
                    return

            index.seek(8)
            plat = struct.unpack("b", index.read(1))
            index.seek(12)
            endian = plat[0]
            if endian != 0:
                return
            endian_id = ">" if endian == 1 else "<"
            skip_size = struct.unpack(f"{endian_id}I", index.read(4))[0]

            index.seek(skip_size + 8)
            seek_to = struct.unpack(f"{endian_id}I", index.read(4))[0]
            data_size = struct.unpack(f"{endian_id}I", index.read(4))[0]
            hash_count = data_size // 8

            index.seek(seek_to)
            hashes = set()
            for i in range(0, hash_count):
                full_hash = struct.unpack(f"{endian_id}i", index.read(4))[0]
                index.seek(index.tell() + 4)
                hashes.add(full_hash)

            self.indexes[index_id] = hashes
            print(f"loaded {len(hashes)} full paths")

    @staticmethod
    def get_category_id(path: str) -> int:
        if path.startswith("com"):
            return 0x000000
        elif path.startswith("bgc"):
            return 0x010000
        elif path.startswith("bg/"):
            return IndexRepository.get_bg_subcategory_id(path) | (0x2 << 16)
        elif path.startswith("cut"):
            return IndexRepository.get_non_bg_subcategory_id(path, 4) | (0x3 << 16)
        elif path.startswith("cha"):
            return 0x040000
        elif path.startswith("sha"):
            return 0x050000
        elif path.startswith("ui/"):
            return 0x060000
        elif path.startswith("sou"):
            return 0x070000
        elif path.startswith("vfx"):
            return 0x080000
        elif path.startswith("ui_"):
            return 0x090000
        elif path.startswith("exd"):
            return 0x0A0000
        elif path.startswith("gam"):
            return 0x0B0000
        elif path.startswith("mus"):
            return IndexRepository.get_non_bg_subcategory_id(path, 6) | (0x0C << 16)
        elif path.startswith("_sq"):
            return 0x110000
        elif path.startswith("_de"):
            return 0x120000
        else:
            return 0

    @staticmethod
    def get_bg_subcategory_id(path: str) -> int:
        segment_id_index = 3

        if path[3] != "e":
            return 0

        if path[6] == "/":
            expac_id = int(path[5:6]) << 8
            segment_id_index = 7
        elif path[7] == "/":
            expac_id = int(path[5:7]) << 8
            segment_id_index = 8
        else:
            expac_id = 0

        segment_id_str = path[segment_id_index:segment_id_index + 2]
        segment_id = int(segment_id_str)
        return expac_id | segment_id

    @staticmethod
    def get_non_bg_subcategory_id(path: str, first_dir_len: int) -> int:
        if path[first_dir_len] != "e":
            return 0

        if path[first_dir_len + 3] == "/":
            return int(path[first_dir_len + 2:first_dir_len + 3]) << 8

        if path[first_dir_len + 4] == "/":
            return int(path[first_dir_len + 2:first_dir_len + 4]) << 8

        return 0

    def exists(self, path: str) -> ExistsResult:
        index_id = self.get_category_id(path)
        full_hash = crc32(path)
        if index_id in self.indexes:
            if full_hash in self.indexes[index_id]:
                return ExistsResult(index_id, path, full_hash, True)
