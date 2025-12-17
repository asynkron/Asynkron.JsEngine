using Asynkron.JsEngine;

var engine = new JsEngine();
var script = """
    class Animal {
        constructor(name) {
            this.name = name;
        }
        speak() {
            return this.name + " makes a sound";
        }
    }

    class Dog extends Animal {
        constructor(name, breed) {
            super(name);
            this.breed = breed;
        }
        speak() {
            return this.name + " barks";
        }
    }

    let dogs = [];
    for (let i = 0; i < 2000; i++) {
        dogs.push(new Dog("Dog" + i, "Breed" + (i % 10)));
    }
    let sounds = dogs.map(d => d.speak());
    sounds.length;
    """;

var parsed = engine.ParseProgram(script);
await engine.Evaluate(parsed);

for (var iter = 0; iter < 20; iter++)
{
    await engine.Evaluate(parsed);
    Console.Write(".");
}
Console.WriteLine("Done");
