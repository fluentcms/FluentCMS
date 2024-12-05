namespace FluentCMS.Web.Api.Controllers;

public class PersonController : BaseGlobalController
{
    private readonly List<Person> _persons =
    [
        new() { Id=1, FirstName = "John", LastName = "Doe", Age = 30 },
        new() { Id=2, FirstName = "Jane", LastName = "Doe", Age = 25 },
        new() { Id=3, FirstName = "Alice", LastName = "Smith", Age = 35 },
        new() { Id=4, FirstName = "Bob", LastName = "Smith", Age = 40 },
    ];

    [HttpGet]
    public async Task<IApiPagingResult<Person>> GetAll(CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
        return OkPaged(_persons);
    }

    [HttpPost]
    public async Task<IApiResult<Person>> Create([FromBody] Person person, CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
        person.Id = _persons.Max(p => p.Id) + 1;
        _persons.Add(person);
        return Ok(person);
    }

    [HttpPost]
    public async Task<IApiResult<Person>> Update([FromBody] Person person, CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
        var existingPerson = _persons.FirstOrDefault(p => p.Id == person.Id);
        if (existingPerson == null)
        {
            return Ok(person);
        }
        existingPerson.FirstName = person.FirstName;
        existingPerson.LastName = person.LastName;
        existingPerson.Age = person.Age;
        return Ok(existingPerson);
    }

    [HttpGet]
    public async Task<List<Person>> GetAllRaw(CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
        return _persons;
    }

    [HttpPost]
    public async Task<Person> CreateRaw([FromBody] Person person, CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
        person.Id = _persons.Max(p => p.Id) + 1;
        _persons.Add(person);
        return person;
    }

    [HttpPost]
    public async Task<Person> UpdateRaw([FromBody] Person person, CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
        var existingPerson = _persons.FirstOrDefault(p => p.Id == person.Id);
        if (existingPerson == null)
        {
            return person;
        }
        existingPerson.FirstName = person.FirstName;
        existingPerson.LastName = person.LastName;
        existingPerson.Age = person.Age;
        return existingPerson;
    }

    // status 500, example of a method that throw an exception
    [HttpGet]
    public async Task<IApiResult<Person>> GetException500(CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
        throw new Exception("This is an exception");
    }

    // status 500, example of a method that throw an exception
    [HttpGet]
    public async Task<Person> GetException500Raw(CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
        throw new Exception("This is an exception");
    }

    // status 404, example of a method that throw an exception
    [HttpGet]
    public async Task<ActionResult> GetException404Raw(CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
        return new NotFoundResult();
    }
}

public class Person
{
    [Required]
    public int Id { get; set; }
    public string FirstName { get; set; } = default!;
    public string LastName { get; set; } = default!;
    public int Age { get; set; }
}
