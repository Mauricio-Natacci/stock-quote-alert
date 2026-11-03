using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Infrastructure.Email;

internal interface IAlertEmailComposer
{
    EmailContent Compose(PriceAlert alert);
}