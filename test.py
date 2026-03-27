import requests

BASE = "http://localhost:9500"
TOKEN = "334e559dea1c4930009ec24709c6d6ce"
HEADERS = {"Authorization": f"Bearer {TOKEN}"}

# List cameras
cameras = requests.get(f"{BASE}/api/cameras", headers=HEADERS).json()
for cam in cameras:
    print(f"{cam['name']} ({cam['id']})")

# Show first 4 cameras in a 2x2 grid
ids = [cam["id"] for cam in cameras[:4]]
requests.post(f"{BASE}/api/cameras/show",
              json={"cameraIds": ids}, headers=HEADERS)

# Clear after 10 seconds
requests.post(f"{BASE}/api/clear",
              json={"delaySeconds": 10}, headers=HEADERS)