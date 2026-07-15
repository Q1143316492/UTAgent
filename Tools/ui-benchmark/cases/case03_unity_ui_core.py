# unity.ui.core 命令式 — 需先 Play 且 WindowManager.open("WndCreateRole") 或场景已有面板
import unity
from unity.ui.core import WndBase

wnd = WndBase.get("WndCreateRole")
if wnd is None:
    unity.log_error("场景中无 WndCreateRole，请先 WindowManager.open")
else:
    unity.log("unity.ui.core command ok")
    print("unity.ui.core command ok")
