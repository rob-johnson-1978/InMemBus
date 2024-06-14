﻿namespace InMemBus.TestInfrastructure.ComplexWorkflow;

public class GetPurchasedItemsQueryHandler(IInMemBus inMemBus) : IInMemBusMessageHandler<GetPurchasedItemsQuery>
{
    public async Task HandleAsync(GetPurchasedItemsQuery message, CancellationToken cancellationToken)
    {
        await Delayer.DelayAsync();

        var numberOfItems = Faker.RandomNumber.Next(1, 10);

        var items = new List<PurchasedItem>(numberOfItems);

        for (var i = 0; i < numberOfItems; i++)
        {
            var price = decimal.Parse($"{Faker.RandomNumber.Next(10, 500)}.{Faker.RandomNumber.Next(10, 99)}");

            items.Add(new PurchasedItem(Guid.NewGuid(), Guid.NewGuid(), price));
        }

        await inMemBus.SendAsync(new PurchasedItemsQueryResult(message.PurchaseId, items));
    }
}