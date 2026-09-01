import json
import sys
from pathlib import Path

request = json.loads(sys.stdin.readline())
if request.get("operation") == "render":
    output_directory = Path(request["job"]["outputDirectory"])
    output_directory.mkdir(parents=True, exist_ok=True)
    rendered_files = []
    for page_number in (1, 2):
        path = output_directory / f"page-{page_number}.png"
        path.write_bytes(b"fake png")
        rendered_files.append(str(path.resolve()))
    response = {
        "protocolVersion": request["protocolVersion"],
        "renderedFiles": rendered_files,
    }
    sys.stdout.write(json.dumps(response) + "\n")
    sys.stdout.flush()
    raise SystemExit(0)

response = {
    "protocolVersion": request["protocolVersion"],
    "analysis": {
        "workerVersion": "fake-1.0",
        "parserVersion": "fake-parser-1.0",
        "textBlocks": [
            {
                "text": "EIRI-INV-001",
                "page": 1,
                "bounds": {"x": 0, "y": 0, "width": 100, "height": 20},
                "confidence": 1.0,
                "source": "pdf-text",
            }
        ],
        "candidates": [],
        "needsReview": True,
    },
}
sys.stdout.write(json.dumps(response) + "\n")
sys.stdout.flush()
