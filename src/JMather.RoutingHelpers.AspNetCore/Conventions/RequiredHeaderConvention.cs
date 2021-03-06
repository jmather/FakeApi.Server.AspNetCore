using System.Linq;
using JMather.RoutingHelpers.AspNetCore.Annotations;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace JMather.RoutingHelpers.AspNetCore.Conventions
{
    public class RequiredHeaderConvention : IActionModelConvention
    {
        public void Apply(ActionModel action)
        {
            var constrainedParameters = action.Attributes.OfType<RequiredHeaderAttribute>();

            foreach (var constrainedParameter in constrainedParameters)
            {
                foreach (var selector in action.Selectors)
                {
                    selector.ActionConstraints.Add(constrainedParameter.GetConstraint());
                }
            }
        }
    }
}