import importlib.machinery
import importlib.util
import sqlite3
import tempfile
import unittest
from pathlib import Path


def load_helper():
    helper_path = Path(__file__).parents[1] / "thaliak-set-sqex-account"
    loader = importlib.machinery.SourceFileLoader("thaliak_set_sqex_account", str(helper_path))
    spec = importlib.util.spec_from_loader(loader.name, loader)
    module = importlib.util.module_from_spec(spec)
    loader.exec_module(module)
    return module


HELPER = load_helper()


class AccountHelperTests(unittest.TestCase):
    def setUp(self):
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.database_path = Path(self.temporary_directory.name) / "thaliak.db"
        self.connection = sqlite3.connect(self.database_path)
        self.connection.execute(
            """
            create table accounts (
                id integer primary key autoincrement,
                purpose text not null unique,
                username text not null,
                password text not null
            )
            """
        )

    def tearDown(self):
        self.connection.close()
        self.temporary_directory.cleanup()

    def test_upsert_account_preserves_other_role(self):
        HELPER.upsert_account(self.connection, "Routine", "trial", "trial-password")
        HELPER.upsert_account(self.connection, "Expansion", "full", "full-password")
        HELPER.upsert_account(self.connection, "Expansion", "replacement", "replacement-password")
        self.connection.commit()

        rows = self.connection.execute(
            "select purpose, username from accounts order by purpose"
        ).fetchall()

        self.assertEqual(
            [("Expansion", "replacement"), ("Routine", "trial")],
            rows,
        )


if __name__ == "__main__":
    unittest.main()
