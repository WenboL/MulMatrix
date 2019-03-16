using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

public class dataResponse
{
    public int[] value { get; set; }
    public string cause { get; set; }
    public bool success { get; set; }
}

public class MatrixCal
{
    public static HttpClient client;
    private static int r = 1000;

    public MatrixCal(int size)
    {
        r = size;
        client = new HttpClient();
        client.BaseAddress = new Uri("https://recruitment-test.investcloud.com/api/numbers/");
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private async Task<int[]> GetRowAsync(int index)
    {
        string path = "B/row/" + index;
        HttpResponseMessage response = await client.GetAsync(path);
        if (response.IsSuccessStatusCode)
        {
            return (await response.Content.ReadAsAsync<dataResponse>()).value;
        }

        return null;
    }

    private async Task<int[]> GetColumnAsync(int index)
    {
        string path = "A/col/" + index;
        HttpResponseMessage response = await client.GetAsync(path);
        if (response.IsSuccessStatusCode)
        {
            return (await response.Content.ReadAsAsync<dataResponse>()).value;
        }

        return null;
    }

    public async Task<bool> InitMatrix(int size)
    {
        string path = "init/" + size;
        HttpResponseMessage response = await client.GetAsync(path);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        return false;
    }

    public async Task<bool> Validate(string answer)
    {
        string path = "validate";
        HttpResponseMessage response = await client.PostAsJsonAsync(
            path, answer);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadAsStringAsync();
        //var result = (await response.Content.ReadAsAsync<dataResponse>()).value;

        Console.WriteLine(result);
        return response.IsSuccessStatusCode;
    }

    // Fetch All needed rows and columns to use and multiply the part of it
    // Most costly part, average 9000 ~ 17000 ms per thread 1000/8
    public async Task<int[]> FetchAll(int start, int end)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        int[][] rows = new int[end - start][];
        int[][] cols = new int[end - start][];

        for (int i = 0; i < (end - start); i++)
        {
            rows[i] = GetRowAsync(start + i).Result;
            cols[i] = GetColumnAsync(start + i).Result;
        }
        stopwatch.Stop();
        Console.WriteLine("\nFetch for block {0} ~ {1}. Consumed: {2}ms", start, end, stopwatch.ElapsedMilliseconds);

        Console.WriteLine("\nA with start: " + start + ", end: " + end);
        foreach (var col in cols )//Transpose(cols))
        {
            foreach (var value in col)
            {
                Console.Write(value + "  ");
            }
            Console.Write("\n");
        }
        Console.WriteLine("\nB with start: " + start + ", end: " + end);
        foreach (var row in rows)
        {
            foreach (var value in row)
            {
                Console.Write(value + "  ");
            }
            Console.Write("\n");
        }

        var result = BrutalF(Transpose(cols), rows);

        return result;
    }

    // Transpose A matrix to prepare for multiply, cost about 0 - 1ms per thread while 1000/8
    public int[][] Transpose(int[][] matrix)
    {
        int w = matrix.GetLength(0);
        int h = matrix[0].Length;

        int[][] result = new int[h][];
        for (int i = 0; i < h; i++)
        {
            result[i] = new int[w];
        }

        for (int i = 0; i < w; i++)
        {
            for (int j = 0; j < h; j++)
            {
                result[j][i] = matrix[i][j];
            }
        }
        
        return result;
    }

    // Multiply matrix, cost about 300 - 400ms per thread while 1000/8
    public int[] BrutalF(int[][] mA, int[][] mB)
    {
        var lenC = mB.Length;
        int[] matrix = new int[r * r];

        for (int i = 0; i < r; i++)
        {
            for (int j = 0; j < r; j++)
            {
                for (int k = 0; k < lenC; k++)
                {
                    matrix[r * i + j] += mA[i][k] * mB[k][j];
                }
            }
        }

        //Console.Write("\nBrutalF result: ");
        //foreach (var value in matrix)
        //{
        //    Console.Write(value+", ");
        //}
        //Console.Write(matrix.Length+"||");
        return matrix;
    }
}

public class Program
{
    public static int Size = 10;
    public static int NumOfThread = 1;
    public static int[] Result = new int[Size * Size];
    private static readonly object obj = new object();
    private static List<BackgroundWorker> _workerGroup = new List<BackgroundWorker>();

    private static MatrixCal matrix = new MatrixCal(Size);
    private static Stopwatch stopwatch;

    public static void Main()
    {
        Producer(NumOfThread, Size);
        Console.Read();
    }

    public static void Producer(int numOfThread, int size)
    {
        if (matrix.InitMatrix(size).Result)
        {
            stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < numOfThread; i++)
            {
                int t = i;
                BackgroundWorker bg = new BackgroundWorker();
                bg.DoWork += new DoWorkEventHandler(DoWork);
                bg.RunWorkerCompleted += new RunWorkerCompletedEventHandler(WorkOnComplete);
                _workerGroup.Add(bg);
                bg.RunWorkerAsync(argument: t);
            }
        }
    }

    public static int[] sumUpMatrixes(int[] partialResult)
    {
        lock (obj)
        {
            for (int i = 0; i < Result.Length; i++)
            {
                Result[i] += partialResult[i];
            }

            Console.Write("\nResult: ");
            foreach (var value in Result)
            {
                Console.Write(value + "  ");
            }
            Console.Write("\nResult length: {0}", Result.Length);
        }
        return Result;
    }

    private static void DoWork(object sender, DoWorkEventArgs e)
    {
        int t = (int) e.Argument;
        int[] result = matrix.FetchAll(t * (Size / NumOfThread), (t + 1) * (Size / NumOfThread)).Result;
        sumUpMatrixes(result);
    }

    private static void WorkOnComplete(object sender, RunWorkerCompletedEventArgs e)
    {
        BackgroundWorker bg = (BackgroundWorker) sender;
        _workerGroup.Remove(bg);
        bg.Dispose();
        if (_workerGroup.Count == 0)
        {
            stopwatch.Stop();
            Console.WriteLine("\nTime Consumed: {0}ms", stopwatch.ElapsedMilliseconds);
            var message = PrepareResult(Result);
            //foreach (var val in Result)
            //{
            //    Console.Write(val + ", ");
            //}
            matrix.Validate(message);
        }
    }

    // Hash the result by using md5
    private static string PrepareResult(int[] data)
    {
        string result = string.Join("", data);
        using (MD5 md5Hash = MD5.Create())
        {
            byte[] input = md5Hash.ComputeHash(Encoding.UTF8.GetBytes(result));
            StringBuilder sBuilder = new StringBuilder();
            for (int i = 0; i < input.Length; i++)
            {
                sBuilder.Append(input[i].ToString("x2"));
            }

            result = sBuilder.ToString();
        }

        Console.WriteLine("\n Final Hashed string: {0}", result);
        return result;
    }
}