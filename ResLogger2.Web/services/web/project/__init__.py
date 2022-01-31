import json
import time

from flask import Flask, jsonify, request, Response
from sqlalchemy.dialects.postgresql import insert

from .index_repository import IndexRepository
from .exists_result import ExistsResult
from .model import db, Path
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
    start = time.time_ns()

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

    files_in_request = 0
    files_that_exist = 0
    for txt in cache['Entries']:
        files_in_request = files_in_request + 1
        result = index_repo.exists(txt)
        if result.full_exists:
            files_that_exist = files_that_exist + 1
            stmt = insert(Path)\
                .values(hash=result.full_hash, path=txt, index=result.index_id)\
                .on_conflict_do_nothing()
            db.session.connection().execute(stmt)
        else:
            print(f"does not exist? ({txt})")
    print(f"of {files_in_request} paths, {files_that_exist} exist!")
    db.session.commit()
    end = time.time_ns()
    print(f"request took {(end - start) / 1000000} ms")

    return Response(status=202)


@app.route("/uploadcheck")
def uploadcheck():
    global cache
    return jsonify(cache)

