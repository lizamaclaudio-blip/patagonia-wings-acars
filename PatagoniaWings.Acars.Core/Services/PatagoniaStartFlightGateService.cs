using PatagoniaWings.Acars.Core.Models;

namespace PatagoniaWings.Acars.Core.Services
{
    /// <summary>
    /// Servicio dedicado del gate previo a iniciar vuelo.
    /// Comparte exactamente el mismo ruleset, motor y auditoria del cierre final.
    /// </summary>
    public sealed class PatagoniaStartFlightGateService
    {
        private readonly PatagoniaEvaluationService _evaluationService;

        public PatagoniaStartFlightGateService(PatagoniaEvaluationService? evaluationService = null)
        {
            _evaluationService = evaluationService ?? new PatagoniaEvaluationService();
        }

        public PatagoniaStartFlightGateResult Evaluate(PatagoniaEvaluationInput input, string explicitRulesPath = "")
        {
            return _evaluationService.EvaluateStartFlightGate(input, explicitRulesPath);
        }
    }
}
