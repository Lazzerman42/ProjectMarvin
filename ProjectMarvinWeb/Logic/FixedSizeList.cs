namespace ProjectMarvin.Logic;
/// <summary>
/// This class isn't used right now. It is a more-or-less Threadsafe implementation of List<>
/// The list has a MaxSize, so it can never have more Entries than MaxSize in a FIFO manner.
/// </summary>
/// <typeparam name="T"></typeparam>
public class FixedSizeList<T> : List<T>
{
	private readonly int _maxSize;
	private readonly object _lockObject = new object();

	public FixedSizeList(int maxSize)
	{
		if (maxSize <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(maxSize), "Max size must be greater than zero.");
		}

		_maxSize = maxSize;
	}

	public new void Add(T item)
	{
		lock (_lockObject)
		{
			if (Count >= _maxSize)
			{
				RemoveAt(0); // Remove the oldest item (first item in the list)
			}
			base.Add(item);
		}
	}

	public new void AddRange(IEnumerable<T> collection)
	{
		lock (_lockObject)
		{
			foreach (var item in collection)
			{
				Add(item); // Use the overridden Add method to ensure size limit
			}
		}
	}

	// Provide a thread-safe way to access the list items
	public List<T> GetSnapshot()
	{
		lock (_lockObject)
		{
			return new List<T>(this);
		}
	}
}
