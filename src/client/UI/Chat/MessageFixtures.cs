using Chat.Core.Messaging;
using Chat.Protocol;

namespace Chat.UI.Chat;

// Local timeline filler for layout only. Never persisted or sent to Core/network.
internal static class MessageFixtures
{
    private static MessageRow[]? _rows;

    public static IReadOnlyList<MessageRow> Rows => _rows ??= Create();

    private static MessageRow[] Create()
    {
        var t0 = new DateTimeOffset(2026, 9, 21, 11, 20, 0, TimeSpan.FromHours(8));
        return
        [
            Row("21", Member(0), t0, "先占个位子，方便看消息间距。"),
            Row("22", Member(0), t0.AddMinutes(1), "同一人接着说，应该收成连续行。"),
            Row("23", Member(1), t0.AddMinutes(3), "试一下 **粗体**、`代码` 和 @mika。"),
            Row("24", Member(2), t0.AddMinutes(6),
                "很长一行用来看折行：侧栏、成员列和输入框都在的时候，正文还得把时间和操作条让开。"),
            Row("25", Member(3), t0.AddMinutes(8), "公式顺手看一眼 $E=mc^2$，还有列表：\n- 间距\n- 对比\n- 头像对齐"),
            Row("26", Member(1), t0.AddMinutes(10), "改过一句，时间旁边应出现已编辑。", t0.AddMinutes(12)),
            Row("27", Member(4), t0.AddMinutes(14), "```\nfn greet(name: &str) {\n    println!(\"{name}\");\n}\n```"),
            Row("29", Member(2), t0.AddMinutes(15),
                "HTML 看一眼：<b>粗体</b>、<i>斜体</i>、<u>下划线</u> 和 <a href=\"https://example.com\">链接</a>。<br>第二行。"),
            Row("28", Member(0), t0.AddMinutes(16), "到这里可以看滚动和底部留白。"),
        ];
    }

    private static (Guid Id, string Name, string Username, bool Online) Member(int index) =>
        MemberFixtures.Seeds[index];

    private static MessageRow Row(string suffix, (Guid Id, string Name, string Username, bool Online) author,
        DateTimeOffset created, string content, DateTimeOffset? edited = null)
    {
        var id = Guid.Parse("01900000-0000-7000-8000-0000000000" + suffix);
        var dto = new MessageDto(id, Guid.Empty, author.Id, "text", content, created, edited, null,
            [], [], [], [], null);
        return MessageRow.Fixture(new TimelineItem
        {
            Message = dto,
            Status = SendStatus.Sent,
            LocalId = id
        }, author.Name);
    }
}
