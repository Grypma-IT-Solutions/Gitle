namespace Gitle.Model.Interfaces.Service
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using James;

    public interface IJamesRegistrationService
    {
        int GetTotalMinutesForEmployee(int jamesEmployeeId, int year, int week);
        int GetTotalMinutesForEmployee(int jamesEmployeeId, DateTime day);
        IList<Employee> GetJamesEmployees();
    }
}