using System;
using System.Collections.Generic;
using System.Text;

namespace DemoEti; 
internal class Helper {

    /// <summary>
    /// Method that execute the opertation based on the opChar and update it to represent the correct symbol</summary>
    public static int Dispatch(int left, ref char opChar, int right) {
        if (opChar == 'a' || opChar == '+') {
            opChar = '+';
            return left + right;
        }
        if (opChar == 'd' || opChar == '/') {
            opChar = '/';
            return left / right;
        }
        if (opChar == 'm' || opChar == '*') {
            opChar = '*';
            return left * right;
        }
        if (opChar == 's' || opChar == '-') {
            opChar = '-';
            return left - right;
        }
        if (opChar == 'e' || opChar == '^') {
            opChar = '^';
            return (int)Math.Pow(left, right);
        }
        throw new NotImplementedException();
    }
}
