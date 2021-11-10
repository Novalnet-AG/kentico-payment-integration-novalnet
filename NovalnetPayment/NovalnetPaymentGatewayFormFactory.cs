using CMS;
using CMS.Ecommerce;
using CMS.Ecommerce.Web.UI;

// Registers the custom implementation of IPaymentGatewayFormFactory
[assembly: RegisterImplementation(typeof(IPaymentGatewayFormFactory), typeof(NovalnetPaymentGatewayFormFactory))]

public class NovalnetPaymentGatewayFormFactory : PaymentGatewayFormFactory
{
 

    protected override string GetPath(CMSPaymentGatewayProvider provider)
    {
        // Maps the path to the correct payment gateway form for the custom gateway providers
        if (provider is NovalnetGatewayProvider)
        {
          return "~/NovalnetGatewayForm.ascx";            
        }
        else
        {
            // Calls the base method to map the paths of the default payment gateways
            return base.GetPath(provider);            
        }
    }
}