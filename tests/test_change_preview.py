import types
import unittest

from main import WinCareApp


class ChangePreviewTests(unittest.TestCase):
    def test_preview_lists_changes_and_logs_decision(self):
        logged = []
        app = types.SimpleNamespace(
            confirm=lambda *args, **kwargs: True,
            logger=types.SimpleNamespace(
                log=lambda action, details: logged.append((action, details))),
        )

        approved = WinCareApp.confirm_changes(
            app, "DNS preview", ["Change adapter DNS", "Flush DNS cache"])

        self.assertTrue(approved)
        self.assertIn("Change adapter DNS", logged[0][1])
        self.assertIn("approved", logged[0][1])


if __name__ == "__main__":
    unittest.main()
