"""Checkout URL validation and launch helper."""

import os
import urllib.parse
import webbrowser


CHECKOUT_URL = os.environ.get("WINCAREPRO_CHECKOUT_URL", "")


def validated_checkout_url(url: str = CHECKOUT_URL) -> str:
    parsed = urllib.parse.urlparse(url)
    if parsed.scheme != "https" or not parsed.netloc or parsed.username:
        raise ValueError("Checkout requires a configured public HTTPS URL.")
    return url


def open_checkout(url: str = CHECKOUT_URL) -> bool:
    return webbrowser.open(validated_checkout_url(url), new=2)
