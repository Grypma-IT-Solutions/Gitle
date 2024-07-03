namespace Gitle.Service
{
    using System;
    using System.Collections.Generic;
    using Model.Interfaces.Service;
    using Model.James;

    public class NoJamesRegistrationService : IJamesRegistrationService
    {
        #region Implementation of IJamesRegistrationService

        public int GetTotalMinutesForEmployee(int jamesEmployeeId, int year, int week)
        {
            return 0;
        }

        public int GetTotalMinutesForEmployee(int jamesEmployeeId, DateTime day)
        {
            return 0;
        }

        public IList<Employee> GetJamesEmployees()
        {
            return new List<Employee>();
        }

        #endregion
    }
}