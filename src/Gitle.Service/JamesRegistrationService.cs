namespace Gitle.Service
{
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.Data.SqlClient;
    using Gitle.Model.James;
    using Model.Interfaces.Service;
    using NHibernate;

    public class JamesRegistrationService : IJamesRegistrationService
    {
        private readonly ISession _session;

        public JamesRegistrationService(ISessionFactory sessionFactory)
        {
            _session = sessionFactory.GetCurrentSession();
        }

        public int GetTotalMinutesForEmployee(int jamesEmployeeId, int year, int week)
        {
            var result = 0;

            using (var reader = ExecuteSqlQuery("james", "SELECT SUM(datediff(mi, wd.StartTijd, wd.EindTijd)) + ISNULL((SELECT SUM(r.DuurMinuten) " +
                                                         "FROM [Registratie] r " +
                                                         "JOIN Werkdag wd on wd.Id = r.Werkdag " +
                                                         "JOIN [Week] w on w.Id = wd.[Week] " +
                                                         "WHERE r.RegistratieType IN (0,1,11) " +
                                                         "AND w.Medewerker = " + jamesEmployeeId +
                                                         "AND w.WeekNr = " + week +
                                                         "AND w.Jaar = " + year + "),0) " +
                                                         "FROM Werkdag wd " +
                                                         "JOIN [Week] w on w.Id = wd.[Week] " +
                                                         "JOIN [Medewerker] m on m.Id = w.Medewerker " +
                                                         "WHERE w.Medewerker = " + jamesEmployeeId +
                                                         "AND w.Jaar = " + year +
                                                         "AND w.WeekNr = " + week +
                                                         "GROUP BY w.WeekNr, w.Jaar, w.Medewerker"))
            {
                while (reader.Read())
                {
                    if (!reader.IsDBNull(0))
                        result = reader.GetInt32(0);
                }
            }

            CloseSqlConnection();

            return result;
        }

        public int GetTotalMinutesForEmployee(int jamesEmployeeId, DateTime day)
        {
            var result = 0;

            using (var reader = ExecuteSqlQuery($"james", $"SELECT SUM(datediff(mi, wd.StartTijd, ISNULL(wd.EindTijd, '{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}'))) + ISNULL((SELECT SUM(r.DuurMinuten) " +
                                                         "FROM [Registratie] r " +
                                                         "JOIN Werkdag wd on wd.Id = r.Werkdag " +
                                                         "JOIN [Week] w on w.Id = wd.[Week] " +
                                                         "WHERE r.RegistratieType IN (0,1,11) " +
                                                         "AND w.Medewerker = " + jamesEmployeeId +
                                                         "AND wd.Datum = " + $"'{day.Date:yyyy-MM-dd HH:mm:ss.fff}'" +
                                                         "AND w.Jaar = " + day.Year + "),0) " +
                                                         "FROM Werkdag wd " +
                                                         "JOIN [Week] w on w.Id = wd.[Week] " +
                                                         "JOIN [Medewerker] m on m.Id = w.Medewerker " +
                                                         "WHERE w.Medewerker = " + jamesEmployeeId +
                                                         "AND w.Jaar = " + day.Year +
                                                         "AND wd.Datum = " + $"'{day.Date:yyyy-MM-dd HH:mm:ss.fff}'" +
                                                         "GROUP BY wd.Datum, w.Jaar, w.Medewerker"))
            {
                while (reader.Read())
                {
                    if (!reader.IsDBNull(0))
                        result = reader.GetInt32(0);
                }
            }

            CloseSqlConnection();

            return result;
        }

        public IList<Employee> GetJamesEmployees()
        {
            var result = new List<Employee>();
            
            using (var reader = ExecuteSqlQuery("james", "SELECT Id, Voornaam, Achternaam FROM Medewerker WHERE Actief = 1"))
            {
                while (reader.Read())
                {
                    result.Add(new Employee
                    {
                        Id = (int)reader[0],
                        FirstName = reader[1].ToString(),
                        LastName = reader[2].ToString()
                    });
                }
            }

            CloseSqlConnection();

            return result;

        }


        private SqlConnection _connection;

        private SqlDataReader ExecuteSqlQuery(string connectionStringName, string sqlCommand)
        {
            _connection = new SqlConnection { ConnectionString = ConfigurationManager.ConnectionStrings[connectionStringName].ConnectionString };
            _connection.Open();

            SqlCommand command = new SqlCommand(sqlCommand, _connection);

            return command.ExecuteReader();
        }

        private void CloseSqlConnection()
        {
            _connection?.Close();
        }
    }
}