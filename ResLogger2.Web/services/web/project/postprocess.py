import re
from typing import Set, List

chara_ids = ["c0101", "c0201",  # Hyur Mid
             "c0104", "c0204",  # Hyur Mid kids
             "c0301", "c0401",  # Hyur Highlander
             "c0501", "c0601",  # Elezen
             "c0504", "c0604",  # Elezen kids
             "c0701", "c0801",  # Miqote
             "c0704", "c0804",  # Miqote kids
             "c0901", "c1001",  # Roegadyn
             "c1101", "c1201",  # Lalafell
             "c1301", "c1401",  # Au ra
             "c1304", "c1404",  # Au ra kids
             "c1501", "c1601",  # Hrothgar
             "c1701", "c1801",  # Viera
             ]
re_chara = re.compile(r"c\d{4}(\D)")

loc_ids = ["_en.", "_de.", "_fr.", "_ja."]

tex_suffix = ["_m.tex", "_n.tex", "_d.tex", "_s.tex"]

equip_type = ["_met", "_top", "_glv", "_dwn", "_sho"]

acc_type = ["_ear", "_nek", "_wrs", "_ril", "_rir"]

gen_m = "_m_"
gen_f = "_f_"


def postprocess_internal(paths: Set[str]) -> Set[str]:
    paths_staging = set()
    for path in paths:
        # Character IDs
        # if re.search(re_chara, path):
        #     for chara_id in chara_ids:
        #         paths_staging.add(re.sub(re_chara, chara_id + r"\1", path))

        # UI highres
        if path.startswith("ui/") and path.endswith("tex"):
            if path.endswith("_hr1.tex"):
                paths_staging.add(path.replace("_hr1", ""))
            else:
                paths_staging.add(path.replace(".tex", "_hr1.tex"))

        # Localization
        # paths_staging.union(add_replacements(path, loc_ids))
        # paths_staging.union(add_replacements(path, tex_suffix))
        # paths_staging.union(add_replacements(path, equip_type))
        # paths_staging.union(add_replacements(path, acc_type))


        # m/f voicelines
        # if gen_m in path:
        #     paths_staging.add(path.replace(gen_m, gen_f))
        # elif gen_f in path:
        #     paths_staging.add(path.replace(gen_f, gen_m))

    return paths.union(paths_staging)


def add_replacements(path: str, elements: List[str]) -> Set[str]:
    stg = set()
    element_id = None
    for element in elements:
        if element in path:
            element_id = element
            break
    if element_id is not None:
        for element in elements:
            if element != element_id:
                stg.add(path.replace(element_id, element))
    return stg


def postprocess(paths: Set[str]) -> Set[str]:
    while True:
        presize = len(paths)
        paths = paths.union(postprocess_internal(paths))
        postsize = len(paths)
        if presize == postsize:
            break
    return paths