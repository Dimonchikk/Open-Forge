// Open Forge — номер версії. Його ж перевіряє збирання на GitHub: номер релізу має збігатися з Text.
using System.Reflection;

[assembly: AssemblyTitle("Open Forge")]
[assembly: AssemblyProduct("Open Forge")]
[assembly: AssemblyDescription("Локальні нейромережі на твоєму ПК")]
[assembly: AssemblyCompany("Дмитро Бричнік")]
[assembly: AssemblyCopyright("© 2026 Дмитро Бричнік")]
[assembly: AssemblyVersion("2.8.3.0")]
[assembly: AssemblyFileVersion("2.8.3.0")]

namespace OpenForge
{
    static class AppVersion
    {
        public const string Text = "2.8.3";
        public const string Full = "2.8.3.0";
    }
}
