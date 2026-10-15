using RoutinePlanner.Web.Models;

namespace RoutinePlanner.Web.Services.Scheduling;

/// <summary>Port of utils/schedule/slotManager.js.</summary>
public static class SlotManager
{
    public static void AssignTeacherToSlot(
        RoutineGrid routine, string day, int slotIndex, string section, string subject, Teacher teacher)
    {
        routine[day][slotIndex][section] = new GridAssignment { Subject = subject, Teacher = teacher };
    }

    public static void RemoveTeacherFromSlot(RoutineGrid routine, string day, int slotIndex, string section)
    {
        routine[day][slotIndex][section] = null;
    }
}
