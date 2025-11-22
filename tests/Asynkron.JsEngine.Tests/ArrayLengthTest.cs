using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class ArrayLengthTest(ITestOutputHelper output)
{
    [Fact(Timeout = 10000)]
    public async Task TestStr2BinlLength()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            var chrsz = 8;
            function str2binl(str) {
              var bin = [];
              var mask = (1 << chrsz) - 1;
              for(var i = 0; i < str.length * chrsz; i += chrsz)
                bin[i>>5] |= (str.charCodeAt(i / chrsz) & mask) << (i%32);
              return bin;
            }

            var plainText = 'Rebellious subjects, enemies to peace,\nProfaners of this neighbour-stained steel,--\nWill they not hear? What, ho! you men, you beasts,\nThat quench the fire of your pernicious rage\nWith purple fountains issuing from your veins,\nOn pain of torture, from those bloody hands\nThrow your mistemper\'d weapons to the ground,\nAnd hear the sentence of your moved prince.\nThree civil brawls, bred of an airy word,\nBy thee, old Capulet, and Montague,\nHave thrice disturb\'d the quiet of our streets,\nAnd made Verona\'s ancient citizens\nCast by their grave beseeming ornaments,\nTo wield old partisans, in hands as old,\nCanker\'d with peace, to part your canker\'d hate:\nIf ever you disturb our streets again,\nYour lives shall pay the forfeit of the peace.\nFor this time, all the rest depart away:\nYou Capulet; shall go along with me:\nAnd, Montague, come you this afternoon,\nTo know our further pleasure in this case,\nTo old Free-town, our common judgment-place.\nOnce more, on pain of death, all men depart.';

            for (var i = 0; i <4; i++) {
                plainText += plainText;
            }

            var result = str2binl(plainText);
            'plainText.length=' + plainText.length + ', result.length=' + result.length;
        ");

        output.WriteLine($"Result: {result}");
        Assert.Contains("plainText.length=15824", result?.ToString());
        Assert.Contains("result.length=3956", result?.ToString());
    }
}
