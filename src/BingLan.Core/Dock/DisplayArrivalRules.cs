namespace BingLan.Core.Dock;

/// <summary>
/// Places desktop cards when an arrangement of monitors appears that has no remembered
/// spots for them: a card already fully on a work area stays, the others are gathered
/// into the nearest work area and cascaded so the arrivals do not stack on one another
/// or on the cards that stayed.
/// </summary>
public static class DisplayArrivalRules
{
    /// <summary>Distance between cascade slots, in pixels.</summary>
    public const int CascadeStride = 24;

    public static IReadOnlyList<PixelRect> Gather(
        IReadOnlyList<PixelRect> cards,
        IReadOnlyList<PixelRect> workAreas,
        PixelRect fallback,
        IReadOnlyList<PixelRect>? reserved = null)
    {
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(workAreas);

        var validAreas = workAreas.Where(area => area.HasArea).ToList();
        if (validAreas.Count == 0)
        {
            if (!fallback.HasArea)
            {
                throw new ArgumentException(
                    "At least one work area or a valid fallback must have a positive area.",
                    nameof(workAreas));
            }
            validAreas.Add(fallback);
        }

        // Spots already taken by cards that are not part of this gather, such as ones
        // restored from their remembered position; arrivals must not stack on them.
        var occupied = reserved?.Where(spot => spot.HasArea).ToList() ?? [];
        var targets = new PixelRect[cards.Count];

        // Cards already fully on a work area keep their spot and claim it first.
        var arrivals = new List<int>();
        for (var index = 0; index < cards.Count; index++)
        {
            var card = cards[index];
            if (!card.HasArea)
            {
                throw new ArgumentException(
                    $"Card bounds must have a positive area (index {index}).",
                    nameof(cards));
            }
            if (validAreas.Any(area => FitsInside(card, area)))
            {
                targets[index] = card;
                occupied.Add(card);
            }
            else
            {
                arrivals.Add(index);
            }
        }

        // Cascading follows the reading order of where the cards sat, so the layout of
        // the removed screen is echoed by the order of the arrivals.
        arrivals.Sort((first, second) =>
        {
            var byTop = cards[first].Top.CompareTo(cards[second].Top);
            return byTop != 0 ? byTop : cards[first].Left.CompareTo(cards[second].Left);
        });

        foreach (var index in arrivals)
        {
            var card = cards[index];
            var area = NearestArea(card, validAreas);
            var size = PlacementResolver.ClampInside(card, area);
            targets[index] = Cascade(size, area, occupied);
            occupied.Add(targets[index]);
        }

        return targets;
    }

    // Walks the stride grid of the work area from the slot nearest the card's clamped
    // spot, wrapping once, and takes the first slot clear of the cards already placed;
    // a grid without a single free slot keeps the clamped spot even where it overlaps.
    private static PixelRect Cascade(PixelRect size, PixelRect area, List<PixelRect> occupied)
    {
        var columns = Math.Max(1, (area.Width - size.Width) / CascadeStride + 1);
        var rows = Math.Max(1, (area.Height - size.Height) / CascadeStride + 1);
        var slotCount = columns * rows;
        var startColumn = Math.Clamp((size.Left - area.Left) / CascadeStride, 0, columns - 1);
        var startRow = Math.Clamp((size.Top - area.Top) / CascadeStride, 0, rows - 1);
        var start = (startRow * columns) + startColumn;

        for (var step = 0; step < slotCount; step++)
        {
            var slot = (start + step) % slotCount;
            var left = area.Left + ((slot % columns) * CascadeStride);
            var top = area.Top + ((slot / columns) * CascadeStride);
            var candidate = new PixelRect(left, top, left + size.Width, top + size.Height);
            if (occupied.All(taken => !candidate.Intersects(taken)))
            {
                return candidate;
            }
        }

        return size;
    }

    private static bool FitsInside(PixelRect card, PixelRect area) =>
        card.Left >= area.Left && card.Right <= area.Right
        && card.Top >= area.Top && card.Bottom <= area.Bottom;

    private static PixelRect NearestArea(PixelRect card, List<PixelRect> areas) =>
        areas
            .Select((area, index) => new
            {
                Area = area,
                Index = index,
                Distance = PlacementResolver.DistanceSquared(card, area)
            })
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Index)
            .First()
            .Area;
}
