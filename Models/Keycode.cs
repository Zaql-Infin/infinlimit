using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InfinLimit.Utility
{
    public enum Keycode : int
    {

        ///Left mouse button

        VK_LMB = 0x1,


        ///Right mouse button

        VK_RMB = 0x2,


        ///Control-break processing

        VK_CANCEL = 0x3,


        ///Middle mouse button (three-button mouse)

        VK_MMB = 0x4,


        /// 4 mouse button

        VK_M4 = 0x05,


        /// 5 mouse button

        VK_M5 = 0x06,


        ///BACKSPACE key

        VK_BACK = 0x8,


        ///TAB key

        VK_TAB = 0x9,


        ///CLEAR key

        VK_CLEAR = 0x0C,


        ///ENTER key

        VK_RETURN = 0x0D,


        ///SHIFT key

        //VK_SHIFT = 0x10,


        ///CTRL key

        //VK_CTRL = 0x11,


        ///ALT key

        //VK_MENU = 0x12,


        ///PAUSE key

        VK_PAUSE = 0x13,


        ///CAPS LOCK key

        VK_CAPS = 0x14,


        ///ESC key

        VK_ESC = 0x1B,


        ///SPACEBAR

        VK_SPACE = 0x20,


        ///PAGE UP key

        VK_PGUP = 0x21,


        ///PAGE DOWN key

        VK_PGDN = 0x22,


        ///END key

        VK_END = 0x23,


        ///HOME key

        VK_HOME = 0x24,


        ///LEFT ARROW key

        VK_LEFT = 0x25,


        ///UP ARROW key

        VK_UP = 0x26,


        ///RIGHT ARROW key

        VK_RIGHT = 0x27,


        ///DOWN ARROW key

        VK_DOWN = 0x28,


        ///SELECT key

        VK_SELECT = 0x29,


        ///PRINT key

        VK_PRINT = 0x2A,


        ///EXECUTE key

        VK_EXECUTE = 0x2B,


        ///PRINT SCREEN key

        VK_SNAPSHOT = 0x2C,


        ///INS key

        VK_INS = 0x2D,


        ///DEL key

        VK_DEL = 0x2E,


        ///HELP key

        VK_HELP = 0x2F,


        ///0 key

        VK_0 = 0x30,


        ///1 key

        VK_1 = 0x31,


        ///2 key

        VK_2 = 0x32,


        ///3 key

        VK_3 = 0x33,


        ///4 key

        VK_4 = 0x34,


        ///5 key

        VK_5 = 0x35,


        ///6 key

        VK_6 = 0x36,


        ///7 key

        VK_7 = 0x37,


        ///8 key

        VK_8 = 0x38,


        ///9 key

        VK_9 = 0x39,


        ///A key

        VK_A = 0x41,


        ///B key

        VK_B = 0x42,


        ///C key

        VK_C = 0x43,


        ///D key

        VK_D = 0x44,


        ///E key

        VK_E = 0x45,


        ///F key

        VK_F = 0x46,


        ///G key

        VK_G = 0x47,


        ///H key

        VK_H = 0x48,


        ///I key

        VK_I = 0x49,


        ///J key

        VK_J = 0x4A,


        ///K key

        VK_K = 0x4B,


        ///L key

        VK_L = 0x4C,


        ///M key

        VK_M = 0x4D,


        ///N key

        VK_N = 0x4E,


        ///O key

        VK_O = 0x4F,


        ///P key

        VK_P = 0x50,


        ///Q key

        VK_Q = 0x51,


        ///R key

        VK_R = 0x52,


        ///S key

        VK_S = 0x53,


        ///T key

        VK_T = 0x54,


        ///U key

        VK_U = 0x55,


        ///V key

        VK_V = 0x56,


        ///W key

        VK_W = 0x57,


        ///X key

        VK_X = 0x58,


        ///Y key

        VK_Y = 0x59,


        ///Z key

        VK_Z = 0x5A,


        /// Left Windows Key 

        VK_LWIN = 0x5B,


        /// Right Windows Key

        VK_RWIN = 0x5C,


        /// Applications Key

        VK_APPS = 0x5D,


        /// Computer Sleep Key

        VK_SLEEP = 0x5F,


        ///Numeric keypad 0 key

        VK_NUM0 = 0x60,


        ///Numeric keypad 1 key

        VK_NUM1 = 0x61,


        ///Numeric keypad 2 key

        VK_NUM2 = 0x62,


        ///Numeric keypad 3 key

        VK_NUM3 = 0x63,


        ///Numeric keypad 4 key

        VK_NUM4 = 0x64,


        ///Numeric keypad 5 key

        VK_NUM5 = 0x65,


        ///Numeric keypad 6 key

        VK_NUM6 = 0x66,


        ///Numeric keypad 7 key

        VK_NUM7 = 0x67,


        ///Numeric keypad 8 key

        VK_NUM8 = 0x68,


        ///Numeric keypad 9 key

        VK_NUM9 = 0x69,


        /// Multiply Key

        VK_MULTIPLY = 0x6A,


        /// Add Key

        VK_ADD = 0x6B,



        ///Separator key

        VK_SEPARATOR = 0x6C,


        ///Subtract key

        VK_SUBTRACT = 0x6D,


        ///Decimal key

        VK_DECIMAL = 0x6E,


        ///Divide key

        VK_DIVIDE = 0x6F,


        ///F1 key

        VK_F1 = 0x70,


        ///F2 key

        VK_F2 = 0x71,


        ///F3 key

        VK_F3 = 0x72,


        ///F4 key

        VK_F4 = 0x73,


        ///F5 key

        VK_F5 = 0x74,


        ///F6 key

        VK_F6 = 0x75,


        ///F7 key

        VK_F7 = 0x76,


        ///F8 key

        VK_F8 = 0x77,


        ///F9 key

        VK_F9 = 0x78,


        ///F10 key

        VK_F10 = 0x79,


        ///F11 key

        VK_F11 = 0x7A,


        ///F12 key

        VK_F12 = 0x7B,


        ///F13 key

        VK_F13 = 0x7C,


        ///F14 key

        VK_F14 = 0x7D,


        ///F15 key

        VK_F15 = 0x7E,


        ///F16 key

        VK_F16 = 0x7F,


        ///F17 key

        VK_F17 = 0x80,


        ///F18 key

        VK_F18 = 0x81,


        ///F19 key

        VK_F19 = 0x82,


        ///F20 key

        VK_F20 = 0x83,


        ///F21 key

        VK_F21 = 0x84,


        ///F22 key

        VK_F22 = 0x85,


        ///F23 key

        VK_F23 = 0x86,


        ///F24 key

        VK_F24 = 0x87,


        ///NUM LOCK key

        VK_NUMLOCK = 0x90,


        ///SCROLL LOCK key

        VK_SCROLL = 0x91,


        ///Left SHIFT key

        VK_LSHIFT = 0xA0,


        ///Right SHIFT key

        VK_RSHIFT = 0xA1,


        ///Left CTRL key

        VK_LCTRL = 0xA2,


        ///Right CTRL key

        VK_RCTRL = 0xA3,


        ///Left MENU key

        VK_LALT = 0xA4,


        ///Right MENU key

        VK_RALT = 0xA5,


        ///Windows 2000/XP/2003/Vista/2008: Browser Back key

        //VK_BROWSER_BACK = 0xA6,


        ///Windows 2000/XP/2003/Vista/2008: Browser Forward key

        //VK_BROWSER_FORWARD = 0xA7,


        ///Windows 2000/XP/2003/Vista/2008: Browser Refresh key

        //VK_BROWSER_REFRESH = 0xA8,


        ///Windows 2000/XP/2003/Vista/2008: Browser Stop key

        //VK_BROWSER_STOP = 0xA9,


        ///Windows 2000/XP/2003/Vista/2008: Browser Search key

        //VK_BROWSER_SEARCH = 0xAA,


        ///Windows 2000/XP/2003/Vista/2008: Browser Favorites key

        //VK_BROWSER_FAVORITES = 0xAB,


        ///Windows 2000/XP/2003/Vista/2008: Browser Start and Home key

        //VK_BROWSER_HOME = 0xAC,


        ///Windows 2000/XP/2003/Vista/2008: Volume Mute key

        VK_VOLUME_MUTE = 0xAD,


        ///Windows 2000/XP/2003/Vista/2008: Volume Down key

        VK_VOLUME_DOWN = 0xAE,


        ///Windows 2000/XP/2003/Vista/2008: Volume Up key

        VK_VOLUME_UP = 0xAF,


        ///Windows 2000/XP/2003/Vista/2008: Next Track key

        VK_MEDIA_NEXT_TRACK = 0xB0,


        ///Windows 2000/XP/2003/Vista/2008: Previous Track key

        VK_MEDIA_PREV_TRACK = 0xB1,


        ///Windows 2000/XP/2003/Vista/2008: Stop Media key

        VK_MEDIA_STOP = 0xB2,


        ///Windows 2000/XP/2003/Vista/2008: Play/Pause Media key

        VK_MEDIA_PLAY_PAUSE = 0xB3,


        ///Windows 2000/XP/2003/Vista/2008: Start Mail key

        //VK_LAUNCH_MAIL = 0xB4,


        ///Windows 2000/XP/2003/Vista/2008: Select Media key

        //VK_LAUNCH_MEDIA_SELECT = 0xB5,


        ///Windows 2000/XP/2003/Vista/2008: Start Application 1 key

        //VK_LAUNCH_APP1 = 0xB6,


        ///Windows 2000/XP/2003/Vista/2008: Start Application 2 key

        //VK_LAUNCH_APP2 = 0xB7,


        ///Windows 2000/XP/2003/Vista/2008: For the US standard keyboard, the ';:' key

        VK_SEMICOLON = 0xBA,


        ///Windows 2000/XP/2003/Vista/2008: For any country/region, the '+' key

        VK_PLUS = 0xBB,


        ///Windows 2000/XP/2003/Vista/2008: For any country/region, the ',' key

        VK_COMMA = 0xBC,


        ///Windows 2000/XP/2003/Vista/2008: For any country/region, the '-' key

        VK_MINUS = 0xBD,


        ///Windows 2000/XP/2003/Vista/2008: For any country/region, the '.' key

        VK_PERIOD = 0xBE,


        ///Windows 2000/XP/2003/Vista/2008: For the US standard keyboard, the '/?' key

        VK_SLASH = 0xBF,


        ///Windows 2000/XP/2003/Vista/2008: For the US standard keyboard, the '`~' key

        VK_TILDE = 0xC0,


        ///Windows 2000/XP/2003/Vista/2008: For the US standard keyboard, the '[{' key

        VK_BRACEOPEN = 0xDB,


        ///Windows 2000/XP/2003/Vista/2008: For the US standard keyboard, the '\|' key

        VK_BACKSLASH = 0xDC,


        ///Windows 2000/XP/2003/Vista/2008: For the US standard keyboard, the ']}' key

        VK_BRACECLOSE = 0xDD,


        ///Windows 2000/XP/2003/Vista/2008: For the US standard keyboard, the 'single-quote/double-quote' key

        VK_QUOTE = 0xDE,


        ///Used for miscellaneous characters; it can vary by keyboard.

        VK_OEM_8 = 0xDF,


        ///Windows 2000/XP/2003/Vista/2008: Either the angle bracket key or the backslash key on the RT 102-key keyboard

        VK_OEM_102 = 0xE2,


        ///Windows 95/98/Me, Windows NT/2000/XP/2003/Vista/2008: IME PROCESS key

        VK_PROCESSKEY = 0xE5,


        ///Windows 2000/XP/2003/Vista/2008: Used to pass Unicode characters as if they were keystrokes. The VK_PACKET key is the low word of a 32-bit Virtual Key value used for non-keyboard input methods. For more information, see Remark in KEYBDINPUT , SendInput , WM_KEYDOWN , and WM_KEYUP

        VK_PACKET = 0xE7,


        ///Only used by Nokia.

        //VK_OEM_RESET = 0xE9,


        ///Only used by Nokia.

        //VK_OEM_JUMP = 0xEA,


        ///Only used by Nokia.

        //VK_OEM_PA1 = 0xEB,


        ///Only used by Nokia.

        //VK_OEM_PA2 = 0xEC,


        ///Only used by Nokia.

        //VK_OEM_PA3 = 0xED,


        ///Only used by Nokia.

        //VK_OEM_WSCTRL = 0xEE,


        ///Only used by Nokia.

        //VK_OEM_CUSEL = 0xEF,


        ///Only used by Nokia.

        //VK_OEM_ATTN = 0xF0,


        ///Only used by Nokia.

        //VK_OEM_FINNISH = 0xF1,


        ///Only used by Nokia.

        //VK_OEM_COPY = 0xF2,


        ///Only used by Nokia.

        //VK_OEM_AUTO = 0xF3,


        ///Only used by Nokia.

        //VK_OEM_ENLW = 0xF4,


        ///Only used by Nokia.

        //VK_OEM_BACKTAB = 0xF5,


        ///Attn key

        //VK_ATTN = 0xF6,


        ///CrSel key

        //VK_CRSEL = 0xF7,


        ///ExSel key

        //VK_EXSEL = 0xF8,


        ///Erase EOF key

        //VK_EREOF = 0xF9,


        ///Play key

        VK_PLAY = 0xFA,


        ///Zoom key

        VK_ZOOM = 0xFB,


        ///Reserved for future use.

        //VK_NONAME = 0xFC,


        ///PA1 key

        //VK_PA1 = 0xFD,


        ///Clear key

        VK_OEM_CLEAR = 0xFE
    }
}
