namespace Atria.Script;

// Recovered from the archived Jamie mission patch, SUBEVENT[541882..545199).
// Original CP949 SHA256:2ad9440b1548a4bde0992861118ca016a7053895a0dd55d97d833f7c68e381fa.
// Only quoted display strings use an authored Russian translation. Every other source byte is identical.
// Audit/review: audit/bugs/quest-patch; docs/BUG_QUEST.md. Original archive is never modified.
internal static class Quest63RecoveredSource
{
    private static readonly byte[] source=Convert.FromBase64String(
        "c3ViYV8xNjEoICkNCnsNCmludCBhLCBiOw0KDQoJYSA9IGdldF9nbG9iYWwoIDkwMSApOw0KDQoJaWYgKCBhID09IDEgKQ0KCXsN" +
        "CgkJc2V0X2dsb2JhbCggOTAxLCAyICk7DQoNCgkJZGlhbG9ndWVfc2NyaXB0KCAxLCAxMzMsDQoJCQkiIFvR++0gzODx8uXw4F0i" +
        "DQoJCQkiIC4uLi4uLiIgKTsNCg0KCQlkaWFsb2d1ZV9zY3JpcHQoIDAsIDEsDQoJCQkiIFvQ7uvg7eRdIg0KCQkJIiDF+eUg7uTt" +
        "4CDy4Org/yD48/Lq4CAtIOgg/yDv8Ojs5e3+IOzg4+j+LiIgKTsNCg0KCQlkaWFsb2d1ZV9zY3JpcHQoIDAsIDMsDQoJCQkiIFvM" +
        "6PHyXSINCgkJCSIg0O7k6PLl6+gg4u7r7fP+8vH/LiDR6u7w5eUg7+7p5OXsIg0KCQkJIiDk7uzu6S4iICk7DQoNCgkJZGlhbG9n" +
        "dWVfc2NyaXB0KCAxLCAxMzMsDQoJCQkiIFvR++0gzODx8uXw4F0iDQoJCQkiIM3l8iEiDQoJCQkiIM7v//L8IOzu7fHy8PshIiAp" +
        "Ow0KDQoJCWRpYWxvZ3VlX3NjcmlwdCggMCwgMSwNCgkJCSIgW9Du6+Dt5F0iDQoJCQkiIMTz7ODl+PwsIOz7IPHt7uLgIO/u7+Dk" +
        "5ezx/z8iDQoJCQkiIMzo8fIsIO/w6OPu8u7i/CDs4OPo/i4iICk7DQoNCgkJZGlhbG9ndWVfc2NyaXB0KCAwLCAzLA0KCQkJIiBb" +
        "zOjx8l0iDQoJCQkiIM/u9e7m5Swg7eAg/fLu8iDw4Ocg7u3oIO3g8fLu//no5S4iICk7DQoNCgkJZGlhbG9ndWVfc2NyaXB0KCAx" +
        "LCA2MSwNCgkJCSIgW8Hg7eTo8l0iDQoJCQkiIML74ejw4Ony5Tog7vLk4PL8IOLx5SDo6+gg6+j46PL88f8g5ujn7eghIiApOw0K" +
        "DQoJCWRpYWxvZ3VlX3NjcmlwdCggMCwgMSwNCgkJCSIgW9Du6+Dt5F0iDQoJCQkiIMLx5SDi8OXs/yDu5O3oIO3l7/Do//Lt7vHy" +
        "6C4iICk7DQoNCgkJYmF0dGxlX3N0YXJ0MyggMTU0ICk7DQoJCQ0KCQlhID0gZ2V0X2dsb2JhbCggNjEgKTsNCgkJaWYgKCBhID09" +
        "IDEgKQ0KCQl7DQoJCQlkaWFsb2d1ZV9zY3JpcHQoIDAsIDEsDQoJCQkJIiBb0O7r4O3kXSINCgkJCQkiIM7v//L8IPPh5ebg6z8i" +
        "DQoJCQkJIiAiIA0KCQkJCSIgIg0KCQkJCSIgIg0KCQkJCSIgzOjx8iwg7eXr/Of/IPHt7uLgIOgg8e3u4uAiDQoJCQkJIiDy8ODy" +
        "6PL8IO3gIP3y7iDi8OXs/y4iDQoJCQkJIiAiDQoJCQkJIiAiDQoJCQkJIiDKIPHr5eTz/vnl6SDi8fLw5fflIMzl8uXu8Ojy7fvp" +
        "IPPk4PAiDQoJCQkJIiDv7uTj7vLu4vwuIiApOw0KCQkJDQoJCQlkaWFsb2d1ZV9zY3JpcHQoIDAsIDMsDQoJCQkJIiBbzOjx8l0i" +
        "DQoJCQkJIiDP7vXu5uUsIPL7IPHo6/zt7iDw4Ofu5+vo6/H/LiINCgkJCQkiICINCgkJCQkiICINCgkJCQkiICINCgkJCQkiIN8g" +
        "4fsg6CDx4Owg9e7y5essIO3uIPMg7OXt/yDv7urgIg0KCQkJCSIg7eUg9eLg8uDl8iDz8O7i7f8uIg0KCQkJCSIgIg0KCQkJCSIg" +
        "Ig0KCQkJCSIgwu/w5eT8IO7y5ODi4Okg4uXx/CDu7/vyIOzt5S4iICk7DQoJCQlkaWFsb2d1ZV9zY3JpcHQoIDAsIDEsDQoJCQkJ" +
        "IiBb0O7r4O3kXSINCgkJCQkiIM/u9e7m5Swg7u0g8+Hl5uDrIO3gIPHl4uXw7i3i7vHy7uouIg0KCQkJCSIgyOTl7CDx6u7w5eUu" +
        "IiApOw0KCQl9CQkJCQ0KCX0NCiAgICANCiAgICBzY3JpcHRfZW5kKCApOw0KICAgIHJldHVybiggMSApOw0KfQ0KDQpzdWJhXzE2" +
        "MiggKQ0Kew0KaW50IGEsIGI7DQoNCglhID0gZ2V0X2dsb2JhbCggOTAxICk7DQoNCglpZiAoIGEgPT0gMiApDQoJew0KCQlzZXRf" +
        "Z2xvYmFsKCA5MDEsIDMgKTsNCg0KCQlkaWFsb2d1ZV9zY3JpcHQoIDEsIDEzMywNCgkJCSIgW9H77SDM4PHy5fDgXSINCgkJCSIg" +
        "yCDv8ODi5OAg7eDx8vvw7fvlLi4uLi4iICk7DQoNCgkJZGlhbG9ndWVfc2NyaXB0KCAwLCAxLA0KCQkJIiBb0O7r4O3kXSINCgkJ" +
        "CSIgzOjx8iwg8eXp9+DxISIgKTsNCg0KCQlkaWFsb2d1ZV9zY3JpcHQoIDEsIDEzMywNCgkJCSIgW9H77SDM4PHy5fDgXSINCgkJ" +
        "CSIgPyEiICk7DQoNCgkJZGlhbG9ndWVfc2NyaXB0KCAwLCAzLA0KCQkJIiBbzOjx8l0iDQoJCQkiIC4uLi4uLiINCgkJCSIgIg0K" +
        "CQkJIiAiDQoJCQkiICINCgkJCSIgw+7y7uLuLiINCgkJCSIgz+7r8/fg6SEiICk7DQoNCgkJZGlhbG9ndWVfc2NyaXB0KCAxLCAx" +
        "MzMsDQoJCQkiIFvR++0gzODx8uXw4F0iDQoJCQkiINP1fiIgKTsNCg0KCQlkaWFsb2d1ZV9zY3JpcHQoIDAsIDEsDQoJCQkiIFvQ" +
        "7uvg7eRdIg0KCQkJIiDP7vXu5uUsIO7tIOrw5e/q7iDz8e3z6y4iDQoJCQkiIN3y7iDs4OPo/yDx7eA/IiApOw0KDQoJCWRpYWxv" +
        "Z3VlX3NjcmlwdCggMCwgMywNCgkJCSIgW8zo8fJdIg0KCQkJIiDE4C4iDQoJCQkiIM7tIOX55SDk7uvj7iDv8O7x7+jyLiINCgkJ" +
        "CSIgIg0KCQkJIiAiDQoJCQkiIMTg4uDpLCDi5+Lg6+gg5ePuIO3gIPHv6O3zLiIgKTsNCg0KCQlkaWFsb2d1ZV9zY3JpcHQoIDAs" +
        "IDEsDQoJCQkiIFvQ7uvg7eRdIg0KCQkJIiDN7iD98u4g7OXx8u4uLi4uLi4iICk7DQoNCgkJZGlhbG9ndWVfc2NyaXB0KCAwLCAz" +
        "LA0KCQkJIiBbzOjx8l0iDQoJCQkiIM/u9e7m5Swg/fLuIPLo4/Do7e7lIOvu4+7i7i4iDQoJCQkiINHq7vDl5SDz9e7k6OwuIiAp" +
        "Ow0KDQoJCWRpYWxvZ3VlX3NjcmlwdCggMCwgMSwNCgkJCSIgW9Du6+Dt5F0iDQoJCQkiIMrg5uXy8f8sIPPm5SDv7ufk7e4uIg0K" +
        "CQkJIiDK4Oog7eDn6+4sIOjs5e3t7iDx5en34PEg7uPw7uzt++kg8ujj8C4uLi4uLiIgKTsNCg0KCQlkaWFsb2d1ZV9zY3JpcHQo" +
        "IDAsIDMsDQoJCQkiIFvM6PHyXSINCgkJCSIgzej35ePuIO3lIO/u5OXr4OX4/C4iICk7DQoNCgkJYmF0dGxlX3N0YXJ0MyggMTU1" +
        "ICk7DQoJCQ0KCQlhID0gZ2V0X2dsb2JhbCggNjEgKTsNCgkJaWYgKCBhID09IDEgKQ0KCQl7DQoJCQlkaWFsb2d1ZV9zY3JpcHQo" +
        "IDAsIDEsDQoJCQkJIiBb0O7r4O3kXSINCgkJCQkiIMXr5SDx7/Dg4ujr6PH8LiINCgkJCQkiICIgDQoJCQkJIiAiDQoJCQkJIiAi" +
        "DQoJCQkJIiAuLi4uLi4hIg0KCQkJCSIgIg0KCQkJCSIgIg0KCQkJCSIgIg0KCQkJCSIgz+735ezzIPMg7eXj7iDi8eUg6+j27iDi" +
        "IOrw7uLoPyIgKTsNCgkJCQ0KCQkJZGlhbG9ndWVfc2NyaXB0KCAwLCAzLA0KCQkJCSIgW8zo8fJdIg0KCQkJCSIgzeDi5fDt7uUs" +
        "IOLuIOLw5ez/IOHu/yDu4/Du7O376SDy6OPwIg0KCQkJCSIg7eAg7eXj7iDt4PHy8+/o6y4iDQoJCQkJIiAiDQoJCQkJIiAiDQoJ" +
        "CQkJIiDN5SDu4fDg+eDpIOLt6Ozg7ej/LCDv7unk5ewg8eru8OXlLiIgKTsNCgkJCWRpYWxvZ3VlX3NjcmlwdCggLTEsIC0xLA0K" +
        "CQkJCSIgIg0KCQkJCSIgx+Dk4O3o5SDi++/u6+3l7e4hIik7DQoNCgkJCXNldF9ldmVudHN0YXR1cyggNjMsIDIgKTsNCgkJCXNl" +
        "dF9ldmVudHN0YXR1cyggMTYxLCAyICk7DQoJCQlzZXRfZXZlbnRzdGF0dXMoIDE2MiwgMiApOw0KCQl9CQkJCQ0KCX0NCiAgICAN" +
        "CiAgICBzY3JpcHRfZW5kKCApOw0KICAgIHJldHVybiggMSApOw0KfQ0KDQo=");
    internal static ReadOnlySpan<byte> Bytes=>source;
    internal static byte[] Append(ReadOnlySpan<byte> original)
    {
        byte[] result=new byte[checked(original.Length+source.Length)];
        original.CopyTo(result);source.CopyTo(result,original.Length);return result;
    }
}
