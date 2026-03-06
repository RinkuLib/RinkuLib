using DemoEti;

int i = 0;
Console.Write("Nbi : ");
int Nbi = int.Parse(Console.ReadLine()!);
Console.Write("NbA : ");
int NbA = int.Parse(Console.ReadLine()!);
while (i < Nbi) {
    Console.Write("Op : ");
    char op = Console.ReadLine()![0];
    if (op == 'c')
        break;
    Console.Write("NbB : ");
    int NbB = int.Parse(Console.ReadLine()!);
    int res = Helper.Dispatch(NbA, ref op, NbB);
    Console.WriteLine($"{NbA} {op} {NbB} = {res}");
    NbA = res;
    i++;
}
Console.WriteLine(NbA);









/*
string result;
int i = 0;
while (i < 3) {
Console.Write("Nb1 : ");
int nbA = int.Parse(Console.ReadLine()!);
Console.Write("Op : ");
char op = Console.ReadLine()![0];
Console.Write("Nb2 : ");
int nbB = int.Parse(Console.ReadLine()!);
result = Tamere(nbA, op, nbB);
Console.WriteLine(result);
i = i + 1;
}
static string GetAdditionString(int palestine, int israel) {
return $"{palestine} + {israel} = {palestine + israel}";
}
static string GetSubstractionString(int palestine, int israel) {
return $"{palestine} - {israel} = {palestine - israel}";
}
static string GetMultiplicationString(int palestine, int israel) {
return $"{palestine} * {israel} = {palestine * israel}";
}
static string GetDivisionString(int palestine, int israel) {
return $"{palestine} / {israel} = {(float)palestine / (float)israel}";
}

static string Tamere(int nbA, char op, int nbB) {
if (op == 'a' || op == '+') {
   return GetAdditionString(nbA, nbB);
}
if ((op == 's') || (op == '-')) {
   return GetSubstractionString(nbA, nbB);
}
if (op == 'm' || op == 'x') {
   return GetMultiplicationString(nbA, nbB);
}
if (op == 'd' || op == '/') {
   return GetDivisionString(nbA, nbB);
}
throw new NotSupportedException();
}
*/