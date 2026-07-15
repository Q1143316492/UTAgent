using System;
using Python.Runtime;
using UnityEngine;
using UTAgent.Editor.Core;

namespace UTAgent.Editor.PythonInterop
{
    public sealed partial class UTAgentPythonBridge
    {
        public static string Reload()
        {
            if (!UTAgentBootstrap.IsAvailable)
            {
                return BridgeJson.Error("引擎不可用");
            }

            try
            {
                using (Py.GIL())
                {
                    dynamic appMod = Py.Import("unity.core.app");
                    appMod.App.reload();
                }

                return BridgeJson.SuccessTrue();
            }
            catch (Exception e)
            {
                return BridgeJson.Error(e.Message);
            }
        }

        public static string Create(int handle, string typeName, string modulePath, string className)
        {
            if (!UTAgentBootstrap.IsAvailable)
            {
                return BridgeJson.Error("引擎不可用，请先初始化");
            }

            try
            {
                using (Py.GIL())
                {
                    dynamic appMod = Py.Import("unity.core.app");
                    dynamic result = appMod.App.create(
                        handle,
                        typeName ?? "UnityBehaviour",
                        modulePath ?? string.Empty,
                        className ?? string.Empty);
                    return PyObjectToJson(result);
                }
            }
            catch (Exception e)
            {
                return BridgeJson.Error(e.Message);
            }
        }

        public static string Dispatch(int handle, string method, string argsJson)
        {
            if (!UTAgentBootstrap.IsAvailable)
            {
                return BridgeJson.Error("引擎不可用，请先初始化");
            }

            try
            {
                using (Py.GIL())
                {
                    dynamic appMod = Py.Import("unity.core.app");
                    dynamic json = Py.Import("json");
                    dynamic args = string.IsNullOrEmpty(argsJson)
                        ? null
                        : json.loads(argsJson);
                    dynamic result = appMod.App.dispatch(handle, method, args);
                    return PyObjectToJson(result);
                }
            }
            catch (Exception e)
            {
                return BridgeJson.Error(e.Message);
            }
        }

        public static string Destroy(int handle)
        {
            if (!UTAgentBootstrap.IsAvailable)
            {
                return BridgeJson.Error("引擎不可用");
            }

            try
            {
                using (Py.GIL())
                {
                    dynamic appMod = Py.Import("unity.core.app");
                    dynamic result = appMod.App.destroy(handle);
                    return PyObjectToJson(result);
                }
            }
            catch (Exception e)
            {
                return BridgeJson.Error(e.Message);
            }
        }

        public static void TickTimers(float deltaTime)
        {
            if (!UTAgentBootstrap.IsAvailable)
            {
                return;
            }

            try
            {
                using (Py.GIL())
                {
                    dynamic appMod = Py.Import("unity.core.app");
                    appMod.App.tick_timers(deltaTime);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UTAgent] Timer tick 失败：{e.Message}");
            }
        }

        private static string PyObjectToJson(PyObject obj)
        {
            dynamic json = Py.Import("json");
            return json.dumps(obj).ToString();
        }
    }
}
