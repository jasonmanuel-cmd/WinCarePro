import unittest

from ai_engine import WinCareAIEngine


class AIEngineSecurityTests(unittest.TestCase):
    def test_ollama_endpoint_is_limited_to_local_http(self):
        self.assertEqual(
            WinCareAIEngine("http://127.0.0.1:11434/").ollama_url,
            "http://127.0.0.1:11434",
        )
        for url in ("file:///etc/passwd", "https://localhost:11434",
                    "http://example.com:11434"):
            with self.subTest(url=url), self.assertRaises(ValueError):
                WinCareAIEngine(url)


if __name__ == "__main__":
    unittest.main()
