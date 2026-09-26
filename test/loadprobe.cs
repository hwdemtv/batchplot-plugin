using System.IO;
namespace BpLoadProbe {
    public class Probe : Autodesk.AutoCAD.Runtime.IExtensionApplication {
        public void Initialize() {
            File.WriteAllText(@"C:\Users\hwdem\.zcode\workspace\default\batchplot-plugin\test\loadprobe.txt",
                "LOADED " + System.DateTime.Now.ToString("HH:mm:ss"));
        }
        public void Terminate() {}
    }
}
