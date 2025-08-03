namespace Respsody.Resp;

public enum RespType : byte
{
    None = 0,

    // +<string>\r\n
    SimpleString = (byte)'+',

    // $<length>\r\n<bytes>\r\n
    BulkString = (byte)'$',

    // -<string>\r\n
    SimpleError = (byte)'-',

    // :<number>\r\n
    Number = (byte)':',

    // _\r\n
    Null = (byte)'_',

    // ,<floating-point-number>\r\n
    Double = (byte)',',

    // #t\r\n
    // #f\r\n
    Boolean = (byte)'#',

    // !<length>\r\n<bytes>\r\n
    BulkError = (byte)'!',

    //        fmt
    // =15\r\n___:some string\r\n
    VerbatimString = (byte)'=',

    // (<big number>\r\n
    BigNumber = (byte)'(',

    // *3\r\n:1\r\n:2\r\n:3\r\n
    Array = (byte)'*',

    // %2<CR><LF>
    // +first<CR><LF>
    // :1<CR><LF>
    // +second<CR><LF>
    // :2<CR><LF>
    Map = (byte)'%',

    // ~5<CR><LF>
    // +orange<CR><LF>
    // +apple<CR><LF>
    // #t<CR><LF>
    // :100<CR><LF>
    // :999<CR><LF>
    Set = (byte)'~',

    // |1<CR><LF>
    //     +key-popularity<CR><LF>
    //     %2<CR><LF>
    //         $1<CR><LF>
    //         a<CR><LF>
    //         ,0.1923<CR><LF>
    //         $1<CR><LF>
    //         b<CR><LF>
    //         ,0.0012<CR><LF>
    // *2<CR><LF>
    //     :2039123<CR><LF>
    //     :9543892<CR><LF>
    Attribute = (byte)'|',

    // >3<CR><LF>
    // +message<CR><LF>
    // +somechannel<CR><LF>
    // +this is the message<CR><LF>
    Push = (byte)'>',

    SteamedStringChunk = (byte)';',

    End = (byte)'.'
}