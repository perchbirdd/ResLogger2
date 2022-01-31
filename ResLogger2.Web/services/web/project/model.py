
from flask_sqlalchemy import SQLAlchemy

db = SQLAlchemy()


class Path(db.Model):
    __tablename__ = "paths"

    hash = db.Column(db.Integer, nullable=False, primary_key=True)
    index = db.Column(db.Integer, nullable=False, primary_key=True)
    path = db.Column(db.Text, nullable=False)
