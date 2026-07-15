"""L1 helper：查 WndSettings 计数（E08 幂等断言）。"""
import json
import unity

r = unity.find_objects("WndSettings", echo=False)
print(json.dumps({"count": r.get("count")}, ensure_ascii=False))
