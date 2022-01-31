

class ExistsResult:
    def __init__(self, index_id: int = -1, full_text: str = "", full_hash: int = -1, full_exists: bool = False):
        self.index_id = index_id
        self.full_text = full_text
        self.full_hash = full_hash
        self.full_exists = full_exists

    def file_text(self) -> str:
        return self.full_text[self.full_text.rfind("/"):]

    def folder_text(self) -> str:
        return self.full_text[:self.full_text.rfind("/")+1]
