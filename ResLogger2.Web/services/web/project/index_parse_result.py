from typing import Dict


class IndexElement:
    file = None
    folder = None
    full = None
    file: int
    folder: int
    full: int

    def __init__(self, folder: int = None, file: int = None, full: int = None):
        self.folder = folder
        self.file = file
        self.full = full


class IndexParseResult:
    gamever = None
    gamever: str
    index_id = None
    index_id: int
    hashes: Dict[int, IndexElement]
    full_map = dict()
    file_map = dict()
    full_map: Dict[int, IndexElement]
    file_map: Dict[int, IndexElement]

    def __init__(self, gamever, index_id):
        self.gamever = gamever
        self.index_id = index_id
        self.hashes = dict()

    def __repr__(self):
        return f"{self.gamever} {self.index_id:06x}({self.index_id}) {sum(1 for x in self.hashes.values() if x.file)}" \
               f"|{sum(1 for x in self.hashes.values() if x.full)}"