import json
import time

from flask import Flask, jsonify, request, Response#, render_template
from sqlalchemy.dialects.postgresql import insert

from .index_repository import IndexRepository
from .exists_result import ExistsResult
from .model import db, Path
from .postprocess import postprocess
from .util import decompress

# this code is brought to you by
# https://testdriven.io/blog/dockerizing-flask-with-postgres-gunicorn-and-nginx/
app = Flask(__name__)
app.config.from_object("project.config.Config")
db.init_app(app)
index_repo = IndexRepository(app.config['INDEX_FOLDER'])

cache = None


@app.route("/")
def hello_world():
    return jsonify(hello="worlds")


@app.route("/upload", methods=['POST'])
def upload():
    global cache
    start = time.perf_counter()

    if len(request.data) > 20000:
        return Response(status=413)

    try:
        data = request.data
        data_str = util.decompress(data)
        cache = json.loads(data_str)
        if not util.validate_json_schema(cache):
            return Response(status=415)
    except Exception as e:
        print(e)
        return Response(status=415)

    post_processing = set()
    files_in_request = 0
    files_that_exist = 0
    files_that_are_new = 0
    for txt in cache['Entries']:
        txt: str
        files_in_request = files_in_request + 1
        result = index_repo.exists(txt)
        if result.full_exists:
            post_processing.add(txt)
            files_that_exist = files_that_exist + 1
            stmt = insert(Path)\
                .values(hash=result.full_hash, path=txt, index=result.index_id)\
                .on_conflict_do_nothing()
            result = db.session.connection().execute(stmt)
            files_that_are_new = files_that_are_new + result.rowcount
            if result.rowcount > 0:
                print(f"    new: '{txt}'")
        else:
            print(f"nonexistent: '{txt}'")
    pre = (time.perf_counter() - start) * 1000
    start = time.perf_counter()
    post = postprocess(post_processing)
    p_files_in_request = 0
    p_files_that_exist = 0
    p_files_that_are_new = 0
    for txt in post:
        if txt in cache['Entries']:
            continue
        p_files_in_request = p_files_in_request + 1
        result = index_repo.exists(txt)
        if result.full_exists:
            p_files_that_exist = p_files_that_exist + 1
            stmt = insert(Path) \
                .values(hash=result.full_hash, path=txt, index=result.index_id) \
                .on_conflict_do_nothing()
            result = db.session.connection().execute(stmt)
            p_files_that_are_new = p_files_that_are_new + result.rowcount
            if result.rowcount > 0:
                print(f"[p] new: '{txt}'")
        # else:
        #     print(f"(p) nonexistent: '{txt}'")
    post = (time.perf_counter() - start) * 1000
    print(f"    {files_in_request:03} paths, {files_that_exist:03} exist, {files_that_are_new:03} new ({pre:.2f}ms)")
    if p_files_in_request > 0:
        print(f"[p] {p_files_in_request:03} paths, {p_files_that_exist:03} exist, {p_files_that_are_new:03} new ({post:.2f}ms)")
    db.session.commit()
    return Response(status=202)


@app.route("/uploadcheck")
def uploadcheck():
    global cache
    return jsonify(cache)


# def get_stats():
#     start = time.time_ns()
#     ret = {}
#     counts = index_repo.get_index_counts()
#
#     query = db.session.query(Path.index, func.count(Path.index)).group_by(Path.index).all()
#     query_results = {x[0]: x[1] for x in query}
#
#     for index_id, count in counts.items():
#         ret[index_id] = {}
#         ret[index_id]['total'] = count
#         ret[index_id]['found'] = query_results[index_id] if index_id in query_results else 0
#
#     flattened = []
#     for index_id in ret.keys():
#         value = ret[index_id]
#         value['id'] = index_id
#         flattened.append(value)
#
#     flattened.sort(key=lambda x: (x['found'] / x['total'] * 100) if x['total'] > 0 else 0, reverse=True)
#
#     print(f"stats took {(time.time_ns() - start) / 1000000:.2f}ms")
#     return flattened


# @app.route("/api/stats")
# def api_stats():
#     return jsonify(get_stats())
#
#
@app.route("/stats")
def stats():
    # return render_template("stats.html", data=get_stats())
    return "sorry stats is disabled because im bad at making databases"
