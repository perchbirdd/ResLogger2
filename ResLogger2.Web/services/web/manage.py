import time

import click
from flask.cli import FlaskGroup

from project import app
from project.index_processor import IndexProcessor
from project.hashdb_sqla import HashDatabaseSQLA
from project.hashdb_pony import HashDatabasePony

cli = FlaskGroup(app)
hd = HashDatabaseSQLA(app)
# hd = HashDatabasePony(app)


@cli.command("create_db")
def create_db():
    hd.create()


@cli.command("ingest")
@click.argument("path")
def ingest(path: str):
    ingest_start = time.perf_counter()
    ip = IndexProcessor(path)

    for index in ip.read():
        print(index)
        hd.process_index(index)

    ingest_stop = time.perf_counter()
    ingest_time = ingest_stop - ingest_start
    print(f"took {ingest_time * 1000}ms")


if __name__ == "__main__":
    cli()
