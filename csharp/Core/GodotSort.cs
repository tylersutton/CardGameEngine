namespace Core;

/// Godot's sort (SortArray in its source: introsort, finished by insertion sort), step for step. It isn't stable,
/// so items that tie end up in an order only this algorithm gives; sorting the same way as GDScript keeps tied
/// cards (two Coppers in a sorted hand) in the same places, which later inputs refer to.
public static class GodotSort
{
    public static void Sort<T>(List<T> items, Func<T, T, bool> less)
    {
        new Sorter<T>(items, less).Sort();
    }

    class Sorter<T>(List<T> items, Func<T, T, bool> less)
    {
        const int IntrosortThreshold = 16;

        public void Sort()
        {
            if (items.Count == 0)
            {
                return;
            }
            Introsort(0, items.Count, BitLog(items.Count) * 2);
            FinalInsertionSort(0, items.Count);
        }

        static int BitLog(int n)
        {
            int k = 0;
            for (; n != 1; n >>= 1)
            {
                k++;
            }
            return k;
        }

        T MedianOf3(T a, T b, T c)
        {
            if (less(a, b))
            {
                if (less(b, c))
                {
                    return b;
                }
                return less(a, c) ? c : a;
            }
            if (less(a, c))
            {
                return a;
            }
            return less(b, c) ? c : b;
        }

        void Introsort(int first, int last, int maxDepth)
        {
            while (last - first > IntrosortThreshold)
            {
                if (maxDepth == 0)
                {
                    PartialSort(first, last, last);
                    return;
                }
                maxDepth--;
                int cut = Partition(first, last, MedianOf3(items[first], items[first + (last - first) / 2], items[last - 1]));
                Introsort(cut, last, maxDepth);
                last = cut;
            }
        }

        int Partition(int first, int last, T pivot)
        {
            while (true)
            {
                while (less(items[first], pivot))
                {
                    first++;
                }
                last--;
                while (less(pivot, items[last]))
                {
                    last--;
                }
                if (first >= last)
                {
                    return first;
                }
                (items[first], items[last]) = (items[last], items[first]);
                first++;
            }
        }

        void FinalInsertionSort(int first, int last)
        {
            if (last - first > IntrosortThreshold)
            {
                InsertionSort(first, first + IntrosortThreshold);
                for (int i = first + IntrosortThreshold; i != last; i++)
                {
                    UnguardedLinearInsert(i, items[i]);
                }
            }
            else
            {
                InsertionSort(first, last);
            }
        }

        void InsertionSort(int first, int last)
        {
            if (first == last)
            {
                return;
            }
            for (int i = first + 1; i != last; i++)
            {
                LinearInsert(first, i);
            }
        }

        void LinearInsert(int first, int last)
        {
            T value = items[last];
            if (less(value, items[first]))
            {
                for (int i = last; i > first; i--)
                {
                    items[i] = items[i - 1];
                }
                items[first] = value;
            }
            else
            {
                UnguardedLinearInsert(last, value);
            }
        }

        void UnguardedLinearInsert(int last, T value)
        {
            int next = last - 1;
            while (less(value, items[next]))
            {
                items[last] = items[next];
                last = next;
                next--;
            }
            items[last] = value;
        }

        // Heapsort, for when quicksort keeps splitting badly.
        void PartialSort(int first, int last, int middle)
        {
            MakeHeap(first, middle);
            for (int i = middle; i < last; i++)
            {
                if (less(items[i], items[first]))
                {
                    PopHeap(first, middle, i, items[i]);
                }
            }
            SortHeap(first, middle);
        }

        void MakeHeap(int first, int last)
        {
            if (last - first < 2)
            {
                return;
            }
            int length = last - first;
            int parent = (length - 2) / 2;
            while (true)
            {
                AdjustHeap(first, parent, length, items[first + parent]);
                if (parent == 0)
                {
                    return;
                }
                parent--;
            }
        }

        void SortHeap(int first, int last)
        {
            while (last - first > 1)
            {
                PopHeap(first, last - 1, last - 1, items[last - 1]);
                last--;
            }
        }

        void PopHeap(int first, int last, int result, T value)
        {
            items[result] = items[first];
            AdjustHeap(first, 0, last - first, value);
        }

        void AdjustHeap(int first, int hole, int length, T value)
        {
            int top = hole;
            int secondChild = 2 * hole + 2;
            while (secondChild < length)
            {
                if (less(items[first + secondChild], items[first + secondChild - 1]))
                {
                    secondChild--;
                }
                items[first + hole] = items[first + secondChild];
                hole = secondChild;
                secondChild = 2 * (secondChild + 1);
            }
            if (secondChild == length)
            {
                items[first + hole] = items[first + secondChild - 1];
                hole = secondChild - 1;
            }
            PushHeap(first, hole, top, value);
        }

        void PushHeap(int first, int hole, int top, T value)
        {
            int parent = (hole - 1) / 2;
            while (hole > top && less(items[first + parent], value))
            {
                items[first + hole] = items[first + parent];
                hole = parent;
                parent = (hole - 1) / 2;
            }
            items[first + hole] = value;
        }
    }
}
