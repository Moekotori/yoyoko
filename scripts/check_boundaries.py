"""Check the small allowed project graph; catches UI/infrastructure leaking into core."""
from pathlib import Path
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
allowed = {
    "Domain": set(), "Protocol": set(), "Localization": set(),
    "Core": {"Domain", "Protocol", "Localization"},
    "Networking": {"Core", "Protocol", "Localization"},
    "Storage": {"Core", "Domain", "Protocol"},
    "Media": {"Core"},
    "Motion": set(),  # Avalonia-only presentation primitives, no business/infrastructure access.
    "UI": {"Core", "Domain", "Protocol", "Localization", "Motion"},
    "App": {"Core", "Networking", "Storage", "Media", "UI", "Localization"},
}
for path in (root / "src/client").glob("*/*.csproj"):
    module = path.parent.name
    for reference in ET.parse(path).iter("ProjectReference"):
        target = (path.parent / reference.attrib["Include"]).resolve().parent.name
        assert target in allowed[module], f"Forbidden dependency: {module} -> {target}"
print("Client dependency boundaries verified.")
