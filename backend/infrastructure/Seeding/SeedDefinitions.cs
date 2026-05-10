using Reviews.Infrastructure.Entities;
using StrongTypes;

namespace Reviews.Infrastructure.Seeding;

// Hand-curated mix so per-product averages span the rating spectrum:
// products 1, 4, 6, 9 average ~4.5+; products 3, 5, 10 average ~1.7-2.3.
internal static class SeedDefinitions
{
    public static readonly Guid Alice = new Guid("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob   = new Guid("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Carol = new Guid("33333333-3333-3333-3333-333333333333");
    public static readonly Guid Dave  = new Guid("44444444-4444-4444-4444-444444444444");
    public static readonly Guid Eve   = new Guid("55555555-5555-5555-5555-555555555555");
    public static readonly Guid Frank = new Guid("66666666-6666-6666-6666-666666666666");
    public static readonly Guid Grace = new Guid("77777777-7777-7777-7777-777777777777");
    public static readonly Guid Henry = new Guid("88888888-8888-8888-8888-888888888888");

    private static readonly Guid[] Rota = [Alice, Bob, Carol, Dave, Eve, Frank, Grace, Henry];

    public static IEnumerable<Product> Products() => ProductRows.Select(r =>
        new Product(
            id:          r.Id,
            slug:        r.Slug.ToNonEmpty(),
            name:        r.Name.ToNonEmpty(),
            description: r.Description.ToNonEmpty(),
            imageUrl:    ProductImage(r.ImageSeed).ToNonEmpty()));

    private record ProductRow(long Id, string Slug, string Name, string Description, string ImageSeed);

    private static readonly ProductRow[] ProductRows =
    [
        new ProductRow(Id: 1,  Slug: "sony-wh-1000xm5",         Name: "Sony WH-1000XM5 Wireless Headphones",   Description: "Flagship over-ear ANC headphones with industry-leading noise cancellation and 30-hour battery life.", ImageSeed: "sony-wh"),
        new ProductRow(Id: 2,  Slug: "usb-c-cable-pack",        Name: "USB-C Cable 3-Pack (1m)",               Description: "Three braided USB-C to USB-C cables, 60W PD, 480Mbps data.",                                          ImageSeed: "usbc-cable"),
        new ProductRow(Id: 3,  Slug: "acme-smartwatch",         Name: "Acme Pro Smartwatch",                   Description: "Fitness tracking, heart-rate monitoring, GPS, and a 1.4-inch AMOLED display.",                        ImageSeed: "acme-watch"),
        new ProductRow(Id: 4,  Slug: "logi-mx-master-3s",       Name: "Logitech MX Master 3S",                 Description: "Ultra-quiet clicks, 8K-DPI sensor, MagSpeed scroll wheel, multi-device pairing.",                     ImageSeed: "logi-mx"),
        new ProductRow(Id: 5,  Slug: "boombox-mini",            Name: "BoomBox Mini Bluetooth Speaker",        Description: "Compact portable speaker with 6-hour battery and IPX5 splash resistance.",                            ImageSeed: "boombox"),
        new ProductRow(Id: 6,  Slug: "ipad-air-11",             Name: "iPad Air 11\" (M3, 256GB)",             Description: "M3 chip, Liquid Retina display, Apple Pencil Pro support.",                                           ImageSeed: "ipad-air"),
        new ProductRow(Id: 7,  Slug: "xyz-mechanical-keyboard", Name: "XYZ Mechanical Keyboard (Brown switches)", Description: "Hot-swappable 75% layout, PBT keycaps, USB-C and Bluetooth.",                                      ImageSeed: "mechkeyb"),
        new ProductRow(Id: 8,  Slug: "travelpro-tripod",        Name: "TravelPro Phone Tripod",                Description: "Aluminum tripod with phone mount and Bluetooth shutter remote.",                                      ImageSeed: "tripod"),
        new ProductRow(Id: 9,  Slug: "single-origin-coffee",    Name: "Highland Single-Origin Coffee 1kg",     Description: "Whole-bean Ethiopia Yirgacheffe, light roast, brewed within 14 days of roasting.",                    ImageSeed: "coffee"),
        new ProductRow(Id: 10, Slug: "powerjuice-10000",        Name: "PowerJuice 10000 Mini Power Bank",      Description: "10,000mAh USB-C PD power bank with passthrough charging.",                                            ImageSeed: "powerbank"),
    ];

    public record SeedReviewData(
        long ProductId,
        Guid AuthorId,
        string AuthorName,
        Rating Rating,
        string Title,
        string Body,
        int Score,
        DateTime CreatedAt,
        IReadOnlyList<string> ImageSeeds,
        IReadOnlyList<(Guid VoterId, bool IsUpvote)> Votes);

    public static IEnumerable<SeedReviewData> Reviews()
    {
        var now = DateTime.UtcNow;
        DateTime daysAgo(int n) => now.AddDays(-n);
        var idx = 0;

        SeedReviewData R(long productId, Guid authorId, string authorName, Rating rating, string title, string body, DateTime createdAt, int helpful, IReadOnlyList<string>? imageSeeds = null)
        {
            var votes = SeedVotesFor(authorId, idx++, helpful);
            return new SeedReviewData(
                ProductId:  productId,
                AuthorId:   authorId,
                AuthorName: authorName,
                Rating:     rating,
                Title:      title,
                Body:       body,
                Score:      votes.Sum(v => v.IsUpvote ? 1 : -1),
                CreatedAt:  createdAt,
                ImageSeeds: imageSeeds ?? Array.Empty<string>(),
                Votes:      votes);
        }

        yield return R(productId: 1, authorId: Alice, authorName: "Alice", rating: Rating.Five, title: "Best ANC on the market", body: "Tried Bose and AirPods Max — the Sony beats both for noise cancelling and the call mics are surprisingly usable.", createdAt: daysAgo(40), helpful: 7);
        yield return R(productId: 1, authorId: Bob,   authorName: "Bob",   rating: Rating.Five, title: "Worth every penny",      body: "Battery actually lasts the full week of commutes. Carry case is small enough to fit in a laptop bag pocket.",       createdAt: daysAgo(32), helpful: 5, imageSeeds: ["sony-r1"]);
        yield return R(productId: 1, authorId: Carol, authorName: "Carol", rating: Rating.Four, title: "Comfortable, slight clamping force", body: "Great pair, only quibble is the clamping force on bigger heads — eased after a week of use.",          createdAt: daysAgo(20), helpful: 4);
        yield return R(productId: 1, authorId: Dave,  authorName: "Dave",  rating: Rating.Five, title: "Replaced my XM4",        body: "Sound is noticeably warmer than the XM4 and the touch controls are easier to learn.",                              createdAt: daysAgo(12), helpful: 3, imageSeeds: ["sony-r2", "sony-r3"]);
        yield return R(productId: 1, authorId: Eve,   authorName: "Eve",   rating: Rating.Four, title: "App is meh",             body: "Headphones are great. The Sony Headphones Connect app keeps demanding updates and re-pairings, which is annoying.", createdAt: daysAgo(5),  helpful: 2);

        // Product 2: USB-C cable pack — mixed, ~3.4 average
        yield return R(productId: 2, authorId: Frank, authorName: "Frank", rating: Rating.Five,  title: "Cheap and they work",     body: "Three of three deliver full 60W to my MacBook. Braiding feels nice.",                                              createdAt: daysAgo(60), helpful: 3);
        yield return R(productId: 2, authorId: Grace, authorName: "Grace", rating: Rating.Three, title: "Fine, not great",         body: "Two work fine, one is finicky and only charges in one orientation. For the price, fine.",                          createdAt: daysAgo(50), helpful: 2);
        yield return R(productId: 2, authorId: Henry, authorName: "Henry", rating: Rating.Two,   title: "One died after a month",  body: "First cable stopped passing data after about a month. Other two still going.",                                     createdAt: daysAgo(30), helpful: 1, imageSeeds: ["usbc-r1"]);
        yield return R(productId: 2, authorId: Alice, authorName: "Alice", rating: Rating.Four,  title: "Decent backups",          body: "Not premium build but works for keeping spares in bags and at desks.",                                             createdAt: daysAgo(15), helpful: 1);
        yield return R(productId: 2, authorId: Bob,   authorName: "Bob",   rating: Rating.Three, title: "Average",                  body: "They charge things. What more do you want?",                                                                       createdAt: daysAgo(6),  helpful: 0);

        // Product 3: Acme smartwatch — bad, ~1.8 average
        yield return R(productId: 3, authorId: Carol, authorName: "Carol", rating: Rating.One,   title: "Battery dies in a day",   body: "Advertised 7-day battery; in reality I get one day with notifications on. Returned mine.",                          createdAt: daysAgo(45), helpful: 7, imageSeeds: ["acme-r1"]);
        yield return R(productId: 3, authorId: Dave,  authorName: "Dave",  rating: Rating.Two,   title: "GPS is useless",           body: "GPS lock takes 3-4 minutes outdoors. By then I'm halfway through my run.",                                          createdAt: daysAgo(35), helpful: 5);
        yield return R(productId: 3, authorId: Eve,   authorName: "Eve",   rating: Rating.One,   title: "App lost my data",         body: "Updated the companion app and lost three months of workouts. No backup option visible anywhere.",                  createdAt: daysAgo(25), helpful: 4);
        yield return R(productId: 3, authorId: Frank, authorName: "Frank", rating: Rating.Three, title: "Screen is nice at least",  body: "Display is genuinely sharp. Everything else about it is bargain-bin.",                                              createdAt: daysAgo(14), helpful: 2);
        yield return R(productId: 3, authorId: Grace, authorName: "Grace", rating: Rating.Two,   title: "Heart rate jumps around",  body: "Heart rate readings during exercise swing 30bpm in a single minute when nothing has changed. Useless for training.", createdAt: daysAgo(8),  helpful: 3);

        // Product 4: Logitech MX Master 3S — excellent, ~4.75
        yield return R(productId: 4, authorId: Henry, authorName: "Henry", rating: Rating.Five, title: "Best mouse I've owned",     body: "Switched from MX Master 2S — quiet click is genuinely quieter, scroll wheel is unchanged (good).",                  createdAt: daysAgo(55), helpful: 7);
        yield return R(productId: 4, authorId: Alice, authorName: "Alice", rating: Rating.Five, title: "Multi-device flow",         body: "Pairing across two laptops + iPad with Flow is the killer feature. Saves me a real five minutes a day.",            createdAt: daysAgo(40), helpful: 5, imageSeeds: ["logi-r1"]);
        yield return R(productId: 4, authorId: Bob,   authorName: "Bob",   rating: Rating.Four, title: "Heavy but in a good way",   body: "Heavier than my old mouse — turns out I prefer it. Build feels long-lasting.",                                       createdAt: daysAgo(22), helpful: 3);
        yield return R(productId: 4, authorId: Carol, authorName: "Carol", rating: Rating.Five, title: "Worth the upgrade",         body: "The horizontal scroll on the side wheel finally has a real use in spreadsheets.",                                    createdAt: daysAgo(10), helpful: 2);

        // Product 5: BoomBox Mini — bad, ~1.5
        yield return R(productId: 5, authorId: Dave,  authorName: "Dave",  rating: Rating.One, title: "Sounds like a tin can",     body: "Bass is non-existent and anything above mid volume distorts.",                                                      createdAt: daysAgo(50), helpful: 6, imageSeeds: ["boombox-r1"]);
        yield return R(productId: 5, authorId: Eve,   authorName: "Eve",   rating: Rating.Two, title: "Battery is honest at least", body: "Battery does last the advertised 6 hours. Unfortunately you'll want to turn it off after one.",                    createdAt: daysAgo(38), helpful: 4);
        yield return R(productId: 5, authorId: Frank, authorName: "Frank", rating: Rating.Two, title: "BT pairing flaky",          body: "Drops connection every time someone walks between me and the phone. Five feet away.",                                createdAt: daysAgo(20), helpful: 3);
        yield return R(productId: 5, authorId: Grace, authorName: "Grace", rating: Rating.One, title: "Returned",                  body: "Volume rocker started intermittently triggering by itself. Returned within a week.",                                  createdAt: daysAgo(7),  helpful: 2);

        // Product 6: iPad Air 11" — good, ~4.5
        yield return R(productId: 6, authorId: Henry, authorName: "Henry", rating: Rating.Five, title: "Hits the sweet spot",       body: "Pro features I actually use, none of the Pro pricing.",                                                              createdAt: daysAgo(60), helpful: 7);
        yield return R(productId: 6, authorId: Alice, authorName: "Alice", rating: Rating.Four, title: "Pencil Pro is great",       body: "Pencil Pro hover and squeeze gestures speed up note-taking real well. Display only 60Hz which I notice scrolling.",   createdAt: daysAgo(35), helpful: 5);
        yield return R(productId: 6, authorId: Bob,   authorName: "Bob",   rating: Rating.Five, title: "Use it more than my laptop now", body: "M3 makes Procreate and Affinity Photo feel desktop-class.",                                                       createdAt: daysAgo(12), helpful: 4, imageSeeds: ["ipad-r1"]);
        yield return R(productId: 6, authorId: Carol, authorName: "Carol", rating: Rating.Four, title: "Wish it had ProMotion",     body: "The 60Hz screen is the only place this still feels behind the Pro.",                                                  createdAt: daysAgo(4),  helpful: 3);

        // Product 7: XYZ Mechanical Keyboard — mixed/good, ~4.0
        yield return R(productId: 7, authorId: Dave,  authorName: "Dave",  rating: Rating.Five,  title: "Hot-swap is the move",      body: "Bought it stock with browns, swapped to silent reds in five minutes without solder. Build is metal where it counts.", createdAt: daysAgo(48), helpful: 6, imageSeeds: ["keyb-r1"]);
        yield return R(productId: 7, authorId: Eve,   authorName: "Eve",   rating: Rating.Three, title: "Software is rough",         body: "Hardware is great. The companion software for layers and lighting is a Windows-only mess.",                            createdAt: daysAgo(30), helpful: 4);
        yield return R(productId: 7, authorId: Frank, authorName: "Frank", rating: Rating.Four,  title: "Solid daily driver",        body: "PBT keycaps, real USB-C, real Bluetooth multi-host. Three out of three checked.",                                       createdAt: daysAgo(15), helpful: 3);
        yield return R(productId: 7, authorId: Grace, authorName: "Grace", rating: Rating.Four,  title: "Browns are a bit mushy",    body: "As advertised — they're not bad, just less tactile than I'd hoped. Hot-swap saved this from being a return.",          createdAt: daysAgo(6),  helpful: 2);

        // Product 8: Phone tripod — mixed, ~3.0
        yield return R(productId: 8, authorId: Henry, authorName: "Henry", rating: Rating.Three, title: "Fine for the price",         body: "Light, folds small, holds my phone. Bluetooth shutter pairs once and forgets you exist by next session.",            createdAt: daysAgo(40), helpful: 3);
        yield return R(productId: 8, authorId: Alice, authorName: "Alice", rating: Rating.Four,  title: "Good travel option",         body: "Goes in a backpack pocket. Stable enough for stationary photo, less so for video pans.",                              createdAt: daysAgo(22), helpful: 2, imageSeeds: ["tripod-r1"]);
        yield return R(productId: 8, authorId: Bob,   authorName: "Bob",   rating: Rating.Two,   title: "Phone clamp slips",          body: "With a heavier phone (Pro Max), the clamp creeps backwards over a few minutes and the phone tilts.",                   createdAt: daysAgo(11), helpful: 4);

        // Product 9: Coffee — good, ~4.75
        yield return R(productId: 9, authorId: Carol, authorName: "Carol", rating: Rating.Five, title: "Real fresh",                  body: "Roast date stamped on the bag was four days before delivery. Tastes like it.",                                       createdAt: daysAgo(50), helpful: 5);
        yield return R(productId: 9, authorId: Dave,  authorName: "Dave",  rating: Rating.Five, title: "Bright, fruity, exactly as described", body: "Yirgacheffe done well. Pulls beautiful as espresso, also great as filter.",                                  createdAt: daysAgo(28), helpful: 4, imageSeeds: ["coffee-r1"]);
        yield return R(productId: 9, authorId: Eve,   authorName: "Eve",   rating: Rating.Four, title: "A bit pricey",                 body: "It's good — fairly priced for single-origin but not a bargain.",                                                      createdAt: daysAgo(16), helpful: 2);
        yield return R(productId: 9, authorId: Frank, authorName: "Frank", rating: Rating.Five, title: "Will buy again",               body: "Haven't had a bag this consistent since my last trip to a roaster.",                                                  createdAt: daysAgo(7),  helpful: 3);

        // Product 10: Power bank — bad, ~2.0
        yield return R(productId: 10, authorId: Grace, authorName: "Grace", rating: Rating.One,   title: "Lies about capacity",         body: "Charges my phone less than two full times. 10,000mAh? Sure.",                                                          createdAt: daysAgo(42), helpful: 7, imageSeeds: ["power-r1"]);
        yield return R(productId: 10, authorId: Henry, authorName: "Henry", rating: Rating.Two,   title: "Charges itself slowly",       body: "Takes 7+ hours to recharge from empty over USB-C. Not the worst, but not the \"fast\" they advertise.",                 createdAt: daysAgo(30), helpful: 5);
        yield return R(productId: 10, authorId: Alice, authorName: "Alice", rating: Rating.Three, title: "OK if you keep expectations low", body: "Fine as an emergency thing in a bag. Don't expect to fast-charge a laptop.",                                       createdAt: daysAgo(18), helpful: 3);
        yield return R(productId: 10, authorId: Bob,   authorName: "Bob",   rating: Rating.Two,   title: "Got hot",                     body: "Got uncomfortably hot mid-charge a couple of times. Stopped using it.",                                                 createdAt: daysAgo(5),  helpful: 4);
    }

    // |helpful| > 7 isn't representable: we only have 8 seed users and the
    // author can't vote on their own review. Voters rotate through Rota
    // starting at `reviewIndex` so different reviews don't all start with Alice.
    private static IReadOnlyList<(Guid VoterId, bool IsUpvote)> SeedVotesFor(Guid authorId, int reviewIndex, int helpful)
    {
        var n = Math.Abs(helpful);
        if (n == 0) return Array.Empty<(Guid, bool)>();
        if (n > Rota.Length - 1)
            throw new ArgumentOutOfRangeException(nameof(helpful), $"|helpful| capped at {Rota.Length - 1} (one less than seed user count)");

        var isUp = helpful > 0;
        var votes = new List<(Guid, bool)>(n);
        for (var i = 0; votes.Count < n; i++)
        {
            var v = Rota[(reviewIndex + i) % Rota.Length];
            if (v != authorId) votes.Add((v, isUp));
        }
        return votes;
    }

    private static string ProductImage(string seed) => $"/api/images/seed/{seed}.jpg";
}
