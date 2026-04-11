using System;
using System.Globalization;
using System.Windows.Data;
using PatagoniaWings.Acars.Core.Enums;

namespace PatagoniaWings.Acars.Master.Helpers
{
    public class PhaseToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is FlightPhase phase)
            {
                return phase switch
                {
                    FlightPhase.Disconnected => "Desconectado",
                    FlightPhase.PreFlight => "Pre-Vuelo",
                    FlightPhase.Boarding => "Embarque",
                    FlightPhase.PushbackTaxi => "Pushback / Taxi",
                    FlightPhase.Takeoff => "Despegue",
                    FlightPhase.Climb => "Ascenso",
                    FlightPhase.Cruise => "Crucero",
                    FlightPhase.Descent => "Descenso",
                    FlightPhase.Approach => "Aproximación",
                    FlightPhase.Landing => "Aterrizaje",
                    FlightPhase.Taxi => "Taxi",
                    FlightPhase.Arrived => "Llegada",
                    FlightPhase.Deboarding => "Desembarque",
                    _ => phase.ToString()
                };
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
