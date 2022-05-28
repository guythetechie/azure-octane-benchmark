using LanguageExt;
using System.Threading.Tasks;

namespace common;

public static class TaskModule
{
    public static async ValueTask<Unit> ToUnitValueTask(this Task task)
    {
        await task;
        return Prelude.unit;
    }
}
