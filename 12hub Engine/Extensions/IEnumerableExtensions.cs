using System;
using System.Collections.Generic;

namespace Hub12.Extensions
{
	public static class IEnumerableExtensions
	{
		public static void Shuffle<T>(this IList<T> list)
		{
			for(var i=0; i < list.Count; i++)
				list.Swap(i, Hub12.TwelveHubEngine.Random.Next(i, list.Count));
		}

		public static void Swap<T>(this IList<T> list, int i, int j)
		{
			var temp = list[i];
			list[i] = list[j];
			list[j] = temp;
		}

		public static T Random<T>(this IList<T> list) =>
			list[Hub12.TwelveHubEngine.Random.Next(0, list.Count)];
	}
}