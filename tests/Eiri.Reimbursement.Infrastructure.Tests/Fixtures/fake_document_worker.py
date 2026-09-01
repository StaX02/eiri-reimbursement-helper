import json
import sys

request = json.loads(sys.stdin.readline())
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
