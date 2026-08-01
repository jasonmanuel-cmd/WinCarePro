# WinCarePro — CI/test Docker image for reproducible build checks.
# Runs tests, security scans, and compiles all Python files.
# The published EXE (WinCarePro.exe) is built natively on Windows
# via PyInstaller; this image does NOT produce that EXE.
#
# Usage:
#   docker build -t wincarepro-ci .
#   docker run --rm -v "${PWD}:/src" wincarepro-ci pytest tests/ -v

FROM python:3.14-slim

WORKDIR /src

RUN apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates \
    && rm -rf /var/lib/apt/lists/*

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt pyinstaller==6.10.0

COPY . .

# Default: run the test suite.
CMD ["pytest", "tests/", "-v", "--tb=short"]
