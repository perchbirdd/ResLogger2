import re
from typing import Set

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

gen_m = "_m_"
gen_f = "_f_"


def postprocess_internal(paths: Set[str]) -> Set[str]:
    paths_staging = set()
    for path in paths:
        # Character IDs
        if re.search(re_chara, path):
            for chara_id in chara_ids:
                paths_staging.add(re.sub(re_chara, chara_id + r"\1", path))

        # UI highres
        if path.startswith("ui/") and path.endswith("tex"):
            if path.endswith("_hr1.tex"):
                paths_staging.add(path.replace("_hr1", ""))
            else:
                paths_staging.add(path.replace(".tex", "_hr1.tex"))

        # Localization
        loc = None
        for loc_id in loc_ids:
            if loc_id in path:
                loc = loc_id
                break
        if loc is not None:
            for loc_id in loc_ids:
                if loc_id != loc:
                    paths_staging.add(path.replace(loc, loc_id))

        # Texture types
        tex = None
        for tex_id in tex_suffix:
            if tex_id in path:
                tex = tex_id
                break
        if tex is not None:
            for tex_id in tex_suffix:
                if tex_id != tex:
                    paths_staging.add(path.replace(tex, tex_id))

        # Equipment codes
        equip = None
        for equip_id in equip_type:
            if equip_id in path:
                equip = equip_id
                break
        if equip is not None:
            for equip_id in equip_type:
                if equip_id != equip:
                    paths_staging.add(path.replace(equip, equip_id))

        # m/f voicelines
        if gen_m in path:
            paths_staging.add(path.replace(gen_m, gen_f))
        elif gen_f in path:
            paths_staging.add(path.replace(gen_f, gen_m))

    return paths.union(paths_staging)


def postprocess(paths: Set[str]) -> Set[str]:
    while True:
        presize = len(paths)
        paths = paths.union(postprocess_internal(paths))
        postsize = len(paths)
        if presize == postsize:
            break
    return paths