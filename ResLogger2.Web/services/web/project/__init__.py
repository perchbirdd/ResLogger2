import json
import time

from flask import Flask, jsonify, request, Response
from sqlalchemy.dialects.postgresql import insert

from .exists_result import ExistsResult
from .util import decompress

# this code is brought to you by
# https://testdriven.io/blog/dockerizing-flask-with-postgres-gunicorn-and-nginx/
app = Flask(__name__)
app.config.from_object("project.config.Config")

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

    files_in_request = 0
    files_that_exist = 0
    files_that_are_new = 0

    print(f"{files_in_request:03} paths, {files_that_exist:03} exist, {files_that_are_new:03} new ({(time.perf_counter() - start) * 1000:.2f}ms)")
    return Response(status=202)


@app.route("/uploadcheck")
def uploadcheck():
    global cache
    return jsonify(cache)

