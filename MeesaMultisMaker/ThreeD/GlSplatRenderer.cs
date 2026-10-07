using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace MeesaMultisMaker.ThreeD
{
    /// <summary>
    /// Exact GPU gaussian-splat rasterizer, ported from SuperSplat
    /// (playcanvas/supersplat, projected-splat-shader.ts):
    ///
    ///  - quad half-axes = 2*sqrt(2*lambda) of the projected covariance
    ///  - per-pixel normalized falloff norm = (exp(-4*r^2) - exp(-4)) / (1 - exp(-4))
    ///  - alpha = norm * splatAlpha, discard r^2 &gt; 1 and alpha &lt; 1/255
    ///  - premultiplied output vec4(color * alpha, alpha), back-to-front,
    ///    blend ONE / ONE_MINUS_SRC_ALPHA
    ///
    /// Runs offscreen (FBO, never shown) on the machine GPU via stock
    /// opengl32.dll + WGL. No new dependencies. Projection math (centers,
    /// covariance eigen-decomposition, depth order) is shared with the
    /// GDI+ path; only the per-pixel evaluation moves to the GPU.
    ///
    /// All entry points are UI-thread only (the GL context is bound there).
    /// Any failure disables the renderer permanently for the session and the
    /// caller falls back to the GDI+ path.
    /// </summary>
    public static class GlSplatRenderer
    {
        public static bool Available { get; private set; }
        public static string StatusNote = string.Empty;

        private static bool _tried;
        private static Form _host;
        private static IntPtr _hdc = IntPtr.Zero;
        private static IntPtr _ctx = IntPtr.Zero;
        private static uint _program;
        private static int _aPos = -1, _aUV = -1, _aCol = -1, _uViewport = -1;

        // Reusable CPU-side scratch (grown as needed, never shrunk).
        private static float[] _verts;
        private static int[] _indices;
        private static int _indicesFor = -1;

        #region WGL / GL imports

        [StructLayout(LayoutKind.Sequential)]
        private struct PIXELFORMATDESCRIPTOR
        {
            public ushort nSize;
            public ushort nVersion;
            public uint dwFlags;
            public byte iPixelType;
            public byte cColorBits;
            public byte cRedBits;
            public byte cRedShift;
            public byte cGreenBits;
            public byte cGreenShift;
            public byte cBlueBits;
            public byte cBlueShift;
            public byte cAlphaBits;
            public byte cAlphaShift;
            public byte cAccumBits;
            public byte cAccumRedBits;
            public byte cAccumGreenBits;
            public byte cAccumBlueBits;
            public byte cAccumAlphaBits;
            public byte cDepthBits;
            public byte cStencilBits;
            public byte cAuxBuffers;
            public sbyte iLayerType;
            public byte bReserved;
            public uint dwLayerMask;
            public uint dwVisibleMask;
            public uint dwDamageMask;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("gdi32.dll")]
        private static extern int ChoosePixelFormat(IntPtr hdc, ref PIXELFORMATDESCRIPTOR pfd);
        [DllImport("gdi32.dll")]
        private static extern bool SetPixelFormat(IntPtr hdc, int format, ref PIXELFORMATDESCRIPTOR pfd);
        [DllImport("opengl32.dll")]
        private static extern IntPtr wglCreateContext(IntPtr hdc);
        [DllImport("opengl32.dll")]
        private static extern bool wglMakeCurrent(IntPtr hdc, IntPtr hglrc);
        [DllImport("opengl32.dll")]
        private static extern IntPtr wglGetProcAddress(string name);
        [DllImport("opengl32.dll")]
        private static extern void glViewport(int x, int y, int w, int h);
        [DllImport("opengl32.dll")]
        private static extern void glClearColor(float r, float g, float b, float a);
        [DllImport("opengl32.dll")]
        private static extern void glClear(uint mask);
        [DllImport("opengl32.dll")]
        private static extern void glEnable(uint cap);
        [DllImport("opengl32.dll")]
        private static extern void glDisable(uint cap);
        [DllImport("opengl32.dll")]
        private static extern void glBlendFunc(uint sf, uint df);
        [DllImport("opengl32.dll")]
        private static extern void glReadPixels(int x, int y, int w, int h, uint format, uint type, byte[] pixels);
        [DllImport("opengl32.dll")]
        private static extern void glGenTextures(int n, uint[] textures);
        [DllImport("opengl32.dll")]
        private static extern void glBindTexture(uint target, uint texture);
        [DllImport("opengl32.dll")]
        private static extern void glTexImage2D(uint target, int level, int internalFormat, int w, int h, int border, uint format, uint type, IntPtr data);
        [DllImport("opengl32.dll")]
        private static extern void glTexParameteri(uint target, uint pname, int param);
        [DllImport("opengl32.dll")]
        private static extern void glDeleteTextures(int n, uint[] textures);
        [DllImport("opengl32.dll")]
        private static extern IntPtr glGetString(uint name);
        [DllImport("opengl32.dll")]
        private static extern uint glGetError();

        private const uint GL_VERSION = 0x1F02;
        private const uint GL_COLOR_BUFFER_BIT = 0x4000;
        private const uint GL_BLEND = 0x0BE2;
        private const uint GL_DEPTH_TEST = 0x0B71;
        private const uint GL_ONE = 1;
        private const uint GL_ONE_MINUS_SRC_ALPHA = 0x0303;
        private const uint GL_TEXTURE_2D = 0x0DE1;
        private const uint GL_RGBA = 0x1908;
        private const uint GL_RGBA8 = 0x8058;
        private const uint GL_UNSIGNED_BYTE = 0x1401;
        private const uint GL_TEXTURE_MIN_FILTER = 0x2801;
        private const uint GL_TEXTURE_MAG_FILTER = 0x2800;
        private const uint GL_NEAREST = 0x2600;
        private const uint GL_FRAMEBUFFER = 0x8D40;
        private const uint GL_COLOR_ATTACHMENT0 = 0x8CE0;
        private const uint GL_FRAMEBUFFER_COMPLETE = 0x8C10;
        private const uint GL_VERTEX_SHADER = 0x8B31;
        private const uint GL_FRAGMENT_SHADER = 0x8B30;
        private const uint GL_COMPILE_STATUS = 0x8B81;
        private const uint GL_LINK_STATUS = 0x8B82;
        private const uint GL_ARRAY_BUFFER = 0x8892;
        private const uint GL_ELEMENT_ARRAY_BUFFER = 0x8893;
        private const uint GL_STATIC_DRAW = 0x88E4;
        private const uint GL_FLOAT = 0x1406;
        private const uint GL_UNSIGNED_INT = 0x1405;
        private const uint GL_TRIANGLES = 0x0004;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint GlCreateShader(uint type);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlShaderSource(uint shader, int count, string[] src, int[] len);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlCompileShader(uint shader);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlGetShaderiv(uint shader, uint pname, out int param);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlGetShaderInfoLog(uint shader, int maxLen, out int len, StringBuilder log);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint GlCreateProgram();
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlAttachShader(uint prog, uint shader);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlLinkProgram(uint prog);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlGetProgramiv(uint prog, uint pname, out int param);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlGetProgramInfoLog(uint prog, int maxLen, out int len, StringBuilder log);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlDeleteShader(uint shader);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlDeleteProgram(uint prog);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlUseProgram(uint prog);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GlGetAttribLocation(uint prog, string name);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GlGetUniformLocation(uint prog, string name);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlUniform2f(int loc, float x, float y);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlEnableVertexAttribArray(uint index);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlVertexAttribPointer(uint index, int size, uint type, bool norm, int stride, IntPtr ptr);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlDisableVertexAttribArray(uint index);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlGenBuffers(int n, uint[] buffers);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlBindBuffer(uint target, uint buffer);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlBufferDataF(uint target, IntPtr size, float[] data, uint usage);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlBufferDataI(uint target, IntPtr size, int[] data, uint usage);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlDeleteBuffers(int n, uint[] buffers);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlGenFramebuffers(int n, uint[] ids);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlBindFramebuffer(uint target, uint fb);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlFramebufferTexture2D(uint target, uint attach, uint texTarget, uint tex, int level);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint GlCheckFramebufferStatus(uint target);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlDeleteFramebuffers(int n, uint[] ids);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlDrawElements(uint mode, int count, uint type, IntPtr indices);

        private static GlCreateShader _glCreateShader;
        private static GlShaderSource _glShaderSource;
        private static GlCompileShader _glCompileShader;
        private static GlGetShaderiv _glGetShaderiv;
        private static GlGetShaderInfoLog _glGetShaderInfoLog;
        private static GlCreateProgram _glCreateProgram;
        private static GlAttachShader _glAttachShader;
        private static GlLinkProgram _glLinkProgram;
        private static GlGetProgramiv _glGetProgramiv;
        private static GlGetProgramInfoLog _glGetProgramInfoLog;
        private static GlDeleteShader _glDeleteShader;
        private static GlDeleteProgram _glDeleteProgram;
        private static GlUseProgram _glUseProgram;
        private static GlGetAttribLocation _glGetAttribLocation;
        private static GlGetUniformLocation _glGetUniformLocation;
        private static GlUniform2f _glUniform2f;
        private static GlEnableVertexAttribArray _glEnableVertexAttribArray;
        private static GlVertexAttribPointer _glVertexAttribPointer;
        private static GlDisableVertexAttribArray _glDisableVertexAttribArray;
        private static GlGenBuffers _glGenBuffers;
        private static GlBindBuffer _glBindBuffer;
        private static GlBufferDataF _glBufferDataF;
        private static GlBufferDataI _glBufferDataI;
        private static GlDeleteBuffers _glDeleteBuffers;
        private static GlGenFramebuffers _glGenFramebuffers;
        private static GlBindFramebuffer _glBindFramebuffer;
        private static GlFramebufferTexture2D _glFramebufferTexture2D;
        private static GlCheckFramebufferStatus _glCheckFramebufferStatus;
        private static GlDeleteFramebuffers _glDeleteFramebuffers;
        private static GlDrawElements _glDrawElements;

        private static T Load<T>(string name) where T : class
        {
            IntPtr p = wglGetProcAddress(name);
            if (p == IntPtr.Zero) throw new Exception("GL entry missing: " + name);
            object d = Marshal.GetDelegateForFunctionPointer(p, typeof(T));
            return (T)d;
        }

        #endregion

        #region Shaders (SuperSplat math)

        private const string VertSrc = @"
attribute vec2 aPos;
attribute vec2 aUV;
attribute vec4 aCol;
uniform vec2 uViewport;
varying vec2 vUV;
varying vec4 vCol;
void main() {
    vUV = aUV;
    vCol = aCol;
    vec2 ndc = vec2(aPos.x / uViewport.x * 2.0 - 1.0, 1.0 - aPos.y / uViewport.y * 2.0);
    gl_Position = vec4(ndc, 0.0, 1.0);
}
";

        // normExp + premultiplied output, exactly as SuperSplat's
        // projected-splat-shader fragment stage.
        private const string FragSrc = @"
varying vec2 vUV;
varying vec4 vCol;
void main() {
    float r2 = dot(vUV, vUV);
    if (r2 > 1.0) discard;
    float norm = (exp(-4.0 * r2) - 0.01831524) / 0.98168476;
    float a = norm * vCol.a;
    if (a < 0.00392157) discard;
    gl_FragColor = vec4(vCol.rgb * a, a);
}
";

        private static uint Compile(uint type, string src)
        {
            uint sh = _glCreateShader(type);
            _glShaderSource(sh, 1, new string[] { src }, new int[] { src.Length });
            _glCompileShader(sh);
            int ok;
            _glGetShaderiv(sh, GL_COMPILE_STATUS, out ok);
            if (ok == 0)
            {
                int len;
                var sb = new StringBuilder(2048);
                _glGetShaderInfoLog(sh, 2048, out len, sb);
                _glDeleteShader(sh);
                throw new Exception("GLSL compile failed: " + sb.ToString());
            }
            return sh;
        }

        #endregion

        private static void EnsureInit()
        {
            if (_tried) { if (!Available) throw new Exception(StatusNote); return; }
            _tried = true;
            try
            {
                _host = new Form();
                _host.Width = 1; _host.Height = 1;
                _host.ShowInTaskbar = false;
                _host.FormBorderStyle = FormBorderStyle.None;
                _host.CreateControl();
                _hdc = GetDC(_host.Handle);
                if (_hdc == IntPtr.Zero) throw new Exception("GetDC failed.");

                var pfd = new PIXELFORMATDESCRIPTOR();
                pfd.nSize = (ushort)Marshal.SizeOf(typeof(PIXELFORMATDESCRIPTOR));
                pfd.nVersion = 1;
                pfd.dwFlags = 0x4 | 0x20 | 0x1; // DRAW_TO_WINDOW | SUPPORT_OPENGL | DOUBLEBUFFER
                pfd.iPixelType = 0;
                pfd.cColorBits = 32;
                pfd.cAlphaBits = 8;
                pfd.cDepthBits = 24;
                int pf = ChoosePixelFormat(_hdc, ref pfd);
                if (pf == 0 || !SetPixelFormat(_hdc, pf, ref pfd))
                    throw new Exception("SetPixelFormat failed.");

                _ctx = wglCreateContext(_hdc);
                if (_ctx == IntPtr.Zero) throw new Exception("wglCreateContext failed.");
                if (!wglMakeCurrent(_hdc, _ctx)) throw new Exception("wglMakeCurrent failed.");

                string ver = Marshal.PtrToStringAnsi(glGetString(GL_VERSION)) ?? "?";

                _glCreateShader = Load<GlCreateShader>("glCreateShader");
                _glShaderSource = Load<GlShaderSource>("glShaderSource");
                _glCompileShader = Load<GlCompileShader>("glCompileShader");
                _glGetShaderiv = Load<GlGetShaderiv>("glGetShaderiv");
                _glGetShaderInfoLog = Load<GlGetShaderInfoLog>("glGetShaderInfoLog");
                _glCreateProgram = Load<GlCreateProgram>("glCreateProgram");
                _glAttachShader = Load<GlAttachShader>("glAttachShader");
                _glLinkProgram = Load<GlLinkProgram>("glLinkProgram");
                _glGetProgramiv = Load<GlGetProgramiv>("glGetProgramiv");
                _glGetProgramInfoLog = Load<GlGetProgramInfoLog>("glGetProgramInfoLog");
                _glDeleteShader = Load<GlDeleteShader>("glDeleteShader");
                _glDeleteProgram = Load<GlDeleteProgram>("glDeleteProgram");
                _glUseProgram = Load<GlUseProgram>("glUseProgram");
                _glGetAttribLocation = Load<GlGetAttribLocation>("glGetAttribLocation");
                _glGetUniformLocation = Load<GlGetUniformLocation>("glGetUniformLocation");
                _glUniform2f = Load<GlUniform2f>("glUniform2f");
                _glEnableVertexAttribArray = Load<GlEnableVertexAttribArray>("glEnableVertexAttribArray");
                _glVertexAttribPointer = Load<GlVertexAttribPointer>("glVertexAttribPointer");
                _glDisableVertexAttribArray = Load<GlDisableVertexAttribArray>("glDisableVertexAttribArray");
                _glGenBuffers = Load<GlGenBuffers>("glGenBuffers");
                _glBindBuffer = Load<GlBindBuffer>("glBindBuffer");
                _glBufferDataF = Load<GlBufferDataF>("glBufferData");
                _glBufferDataI = Load<GlBufferDataI>("glBufferData");
                _glDeleteBuffers = Load<GlDeleteBuffers>("glDeleteBuffers");
                _glGenFramebuffers = Load<GlGenFramebuffers>("glGenFramebuffers");
                _glBindFramebuffer = Load<GlBindFramebuffer>("glBindFramebuffer");
                _glFramebufferTexture2D = Load<GlFramebufferTexture2D>("glFramebufferTexture2D");
                _glCheckFramebufferStatus = Load<GlCheckFramebufferStatus>("glCheckFramebufferStatus");
                _glDeleteFramebuffers = Load<GlDeleteFramebuffers>("glDeleteFramebuffers");
                _glDrawElements = Load<GlDrawElements>("glDrawElements");

                uint vs = Compile(GL_VERTEX_SHADER, VertSrc);
                uint fs = Compile(GL_FRAGMENT_SHADER, FragSrc);
                _program = _glCreateProgram();
                _glAttachShader(_program, vs);
                _glAttachShader(_program, fs);
                _glLinkProgram(_program);
                int linked;
                _glGetProgramiv(_program, GL_LINK_STATUS, out linked);
                _glDeleteShader(vs);
                _glDeleteShader(fs);
                if (linked == 0)
                {
                    int len;
                    var sb = new StringBuilder(2048);
                    _glGetProgramInfoLog(_program, 2048, out len, sb);
                    _glDeleteProgram(_program);
                    throw new Exception("GLSL link failed: " + sb.ToString());
                }

                _aPos = _glGetAttribLocation(_program, "aPos");
                _aUV = _glGetAttribLocation(_program, "aUV");
                _aCol = _glGetAttribLocation(_program, "aCol");
                _uViewport = _glGetUniformLocation(_program, "uViewport");
                if (_aPos < 0 || _aUV < 0 || _aCol < 0 || _uViewport < 0)
                    throw new Exception("GL attribute/uniform lookup failed.");

                Available = true;
                StatusNote = "GPU splat renderer active (" + ver + ")";
            }
            catch (Exception ex)
            {
                Available = false;
                StatusNote = "GPU renderer unavailable (" + ex.Message + "), using CPU path.";
                throw new Exception(StatusNote);
            }
        }

        private struct SortItem
        {
            public float Depth;
            public int Index;
        }

        /// <summary>
        /// Render splats only (transparent background) to a bitmap of the
        /// requested size. Returns null only if the GPU path is unavailable;
        /// the caller then uses the GDI+ renderer. Throws on render errors
        /// (caller falls back too).
        /// </summary>
        /// <param name="uoOblique">True UO projection, false free-orbit camera.</param>
        /// <param name="rotXDeg">Manual object rotation, composed under the facing yaw.</param>
        /// <param name="scX">Manual object scale (1 = unchanged).</param>
        public static Bitmap RenderLayer(PlySplatModel model, float yawDeg, float pitchDeg,
            int width, int height, float zoom, float pointScale, int maxPoints, bool uoOblique,
            float rotXDeg = 0f, float rotYDeg = 0f, float rotZDeg = 0f,
            float scX = 1f, float scY = 1f, float scZ = 1f)
        {
            try
            {
                EnsureInit();
            }
            catch
            {
                return null;
            }

            if (model == null || model.Count == 0 || width <= 0 || height <= 0)
                return null;

            if (!wglMakeCurrent(_hdc, _ctx))
                throw new Exception("wglMakeCurrent failed.");

            double yaw = yawDeg * Math.PI / 180.0;
            double cosY = Math.Cos(yaw), sinY = Math.Sin(yaw);
            double pitch = pitchDeg * Math.PI / 180.0;
            double cosP = Math.Cos(pitch), sinP = Math.Sin(pitch);
            double fit = Math.Min(width, height) * 0.42 * zoom;
            double cx = width * 0.5, cy = height * 0.52;

            // Camera rows for covariance, in MODEL frame (the model yaw must
            // be folded in, exactly like the orbit rows below; fixed rows
            // are only correct at yaw 0 and streak everywhere else).
            double xr0, xr1, xr2, yr0, yr1, yr2;
            if (uoOblique)
            {
                xr0 = cosY + sinY; xr1 = 0; xr2 = sinY - cosY;
                yr0 = cosY - sinY; yr1 = -SplatRenderer.UoVerticalScale; yr2 = sinY + cosY;
            }
            else
            {
                xr0 = cosY; xr1 = 0; xr2 = sinY;
                yr0 = sinP * sinY; yr1 = cosP; yr2 = -sinP * cosY;
            }
            if (scX <= 0) scX = 1f;
            if (scY <= 0) scY = 1f;
            if (scZ <= 0) scZ = 1f;
            double[] robj = SplatRenderer.ObjectMatrix(rotXDeg, rotYDeg, rotZDeg);
            double[] mpos = SplatRenderer.ScaledMatrix(robj, scX, scY, scZ);
            SplatRenderer.FoldRows(robj, ref xr0, ref xr1, ref xr2, ref yr0, ref yr1, ref yr2);

            int count = model.Count;
            int stride = 1;
            if (maxPoints > 0 && count > maxPoints)
                stride = (count + maxPoints - 1) / maxPoints;
            double radiusBoost = Math.Sqrt((double)stride);

            var pts = model.Points;
            var order = new List<SortItem>((count + stride - 1) / stride);
            for (int i = 0; i < count; i += stride)
            {
                var p = pts[i];
                if (p.Opacity < 0.004f) continue;
                double qx, qy, qz;
                SplatRenderer.TransformPoint(mpos, p.X, p.Y, p.Z, out qx, out qy, out qz);
                double x1 = qx * cosY + qz * sinY;
                double z1 = -qx * sinY + qz * cosY;
                double sx, sy, depth;
                if (uoOblique)
                {
                    sx = cx + (x1 - z1) * fit;
                    sy = cy + (x1 + z1) * fit - qy * SplatRenderer.UoVerticalScale * fit;
                    depth = x1 + z1 + qy;
                }
                else
                {
                    double y2 = qy * cosP - z1 * sinP;
                    sx = cx + x1 * fit;
                    sy = cy - y2 * fit;
                    depth = qy * sinP + z1 * cosP;
                }
                // Rough fullscreen cull (quad extent checked per-splat below).
                if (sx < -256 || sy < -256 || sx > width + 256 || sy > height + 256) continue;
                order.Add(new SortItem { Depth = (float)depth, Index = i });
            }
            // Far first for correct alpha blending.
            order.Sort((a, b) => a.Depth.CompareTo(b.Depth));

            int quads = order.Count;
            int needVerts = quads * 4 * 8;
            if (_verts == null || _verts.Length < needVerts)
                _verts = new float[Math.Max(needVerts, 65536)];

            // SuperSplat quad half-extent: 2*sqrt(2*lambda).
            const double AxisK = 2.8284271247461903;
            int vpos = 0;
            int emitted = 0;
            foreach (var so in order)
            {
                var p = pts[so.Index];
                double qx, qy, qz;
                SplatRenderer.TransformPoint(mpos, p.X, p.Y, p.Z, out qx, out qy, out qz);
                double x1 = qx * cosY + qz * sinY;
                double z1 = -qx * sinY + qz * cosY;
                double sx, sy;
                if (uoOblique)
                {
                    sx = cx + (x1 - z1) * fit;
                    sy = cy + (x1 + z1) * fit - qy * SplatRenderer.UoVerticalScale * fit;
                }
                else
                {
                    double y2 = qy * cosP - z1 * sinP;
                    sx = cx + x1 * fit;
                    sy = cy - y2 * fit;
                }

                // Covariance -> eigen (same math as the GDI+ path).
                double w = p.Qw, x = p.Qx, y = p.Qy, z = p.Qz;
                double r00 = 1 - 2 * (y * y + z * z);
                double r01 = 2 * (x * y - w * z);
                double r02 = 2 * (x * z + w * y);
                double r10 = 2 * (x * y + w * z);
                double r11 = 1 - 2 * (x * x + z * z);
                double r12 = 2 * (y * z - w * x);
                double r20 = 2 * (x * z - w * y);
                double r21 = 2 * (y * z + w * x);
                double r22 = 1 - 2 * (x * x + y * y);
                double s0 = p.S0 * scX, s1 = p.S1 * scY, s2 = p.S2 * scZ;
                double m00 = r00 * s0, m01 = r01 * s1, m02 = r02 * s2;
                double m10 = r10 * s0, m11 = r11 * s1, m12 = r12 * s2;
                double m20 = r20 * s0, m21 = r21 * s1, m22 = r22 * s2;
                double c00 = m00 * m00 + m01 * m01 + m02 * m02;
                double c01 = m00 * m10 + m01 * m11 + m02 * m12;
                double c02 = m00 * m20 + m01 * m21 + m02 * m22;
                double c11 = m10 * m10 + m11 * m11 + m12 * m12;
                double c12 = m10 * m20 + m11 * m21 + m12 * m22;
                double c22 = m20 * m20 + m21 * m21 + m22 * m22;
                // Positions are Y-flipped at load (image-space Y-down data
                // in a Y-up viewport). Orientations must follow the same
                // mirror M=diag(1,-1,1): cov' = M*cov*M, i.e. negate the
                // Y cross-terms. Without this, elongated splats lean the
                // wrong way and edges render streaky/furry.
                c01 = -c01;
                c12 = -c12;
                double t0 = c00 * xr0 + c01 * xr1 + c02 * xr2;
                double t1 = c01 * xr0 + c11 * xr1 + c12 * xr2;
                double t2 = c02 * xr0 + c12 * xr1 + c22 * xr2;
                double a = t0 * xr0 + t1 * xr1 + t2 * xr2;
                double b = t0 * yr0 + t1 * yr1 + t2 * yr2;
                double u0 = c00 * yr0 + c01 * yr1 + c02 * yr2;
                double u1 = c01 * yr0 + c11 * yr1 + c12 * yr2;
                double u2 = c02 * yr0 + c12 * yr1 + c22 * yr2;
                double c = u0 * yr0 + u1 * yr1 + u2 * yr2;
                double trace = (a + c) * 0.5;
                double diff = (a - c) * 0.5;
                double disc = Math.Sqrt(Math.Max(0.0, diff * diff + b * b));
                double l1 = Math.Max(1e-12, trace + disc);
                double l2 = Math.Max(1e-12, trace - disc);
                double ang = 0.5 * Math.Atan2(2 * b, a - c);
                double r1 = AxisK * Math.Sqrt(l1) * fit * pointScale * radiusBoost;
                double r2 = AxisK * Math.Sqrt(l2) * fit * pointScale * radiusBoost;
                if (r1 < 1.0) r1 = 1.0;
                if (r2 < 1.0) r2 = 1.0;
                if (r1 > 1024.0) r1 = 1024.0;
                if (r2 > 1024.0) r2 = 1024.0;

                // Tight per-quad cull.
                double ext = (Math.Abs(Math.Cos(ang)) * r1 + Math.Abs(Math.Sin(ang)) * r2);
                double extY = (Math.Abs(Math.Sin(ang)) * r1 + Math.Abs(Math.Cos(ang)) * r2);
                if (sx + ext < 0 || sx - ext > width || sy + extY < 0 || sy - extY > height)
                    continue;

                double ca = Math.Cos(ang), sa = Math.Sin(ang);
                double e1x = ca * r1, e1y = sa * r1;
                double e2x = -sa * r2, e2y = ca * r2;
                float fr = p.R / 255f, fg = p.G / 255f, fb = p.B / 255f;
                var hm = SplatRenderer.HighlightMask;
                if (hm != null && so.Index < hm.Length && hm[so.Index])
                {
                    Color hc = SplatRenderer.HighlightColor;
                    fr = (fr + hc.R / 255f) * 0.5f;
                    fg = (fg + hc.G / 255f) * 0.5f;
                    fb = (fb + hc.B / 255f) * 0.5f;
                }
                float fa = p.Opacity;
                if (fa < 0f) fa = 0f;
                if (fa > 1f) fa = 1f;

                // Corners (-,-),(+,-),(+,+),(-,+) with uv in [-1,1].
                _verts[vpos++] = (float)(sx - e1x - e2x); _verts[vpos++] = (float)(sy - e1y - e2y);
                _verts[vpos++] = -1f; _verts[vpos++] = -1f;
                _verts[vpos++] = fr; _verts[vpos++] = fg; _verts[vpos++] = fb; _verts[vpos++] = fa;
                _verts[vpos++] = (float)(sx + e1x - e2x); _verts[vpos++] = (float)(sy + e1y - e2y);
                _verts[vpos++] = 1f; _verts[vpos++] = -1f;
                _verts[vpos++] = fr; _verts[vpos++] = fg; _verts[vpos++] = fb; _verts[vpos++] = fa;
                _verts[vpos++] = (float)(sx + e1x + e2x); _verts[vpos++] = (float)(sy + e1y + e2y);
                _verts[vpos++] = 1f; _verts[vpos++] = 1f;
                _verts[vpos++] = fr; _verts[vpos++] = fg; _verts[vpos++] = fb; _verts[vpos++] = fa;
                _verts[vpos++] = (float)(sx - e1x + e2x); _verts[vpos++] = (float)(sy - e1y + e2y);
                _verts[vpos++] = -1f; _verts[vpos++] = 1f;
                _verts[vpos++] = fr; _verts[vpos++] = fg; _verts[vpos++] = fb; _verts[vpos++] = fa;
                emitted++;
            }

            // Index buffer matches exactly the emitted quads (the tight cull
            // above may skip some).
            if (_indicesFor != emitted)
            {
                _indices = new int[Math.Max(emitted * 6, 6)];
                for (int q = 0; q < emitted; q++)
                {
                    int v = q * 4, o = q * 6;
                    _indices[o] = v; _indices[o + 1] = v + 1; _indices[o + 2] = v + 2;
                    _indices[o + 3] = v; _indices[o + 4] = v + 2; _indices[o + 5] = v + 3;
                }
                _indicesFor = emitted;
            }
            if (emitted == 0)
                return new Bitmap(width, height, PixelFormat.Format32bppArgb);

            Bitmap result = RenderQuads(_verts, vpos, _indices, emitted * 6, width, height);

            // Bitmap is row-major top-down; GL readback is bottom-up: flip.
            result.RotateFlip(RotateFlipType.RotateNoneFlipY);
            return result;
        }

        private static int _hostW, _hostH;

        private static Bitmap RenderQuads(float[] verts, int floatCount, int[] indices, int indexCount, int width, int height)
        {
            // Render straight into the hidden window's backbuffer (never
            // shown, never swapped). No FBO/texture machinery, no readback
            // mismatch: what the GPU rasterizes is what glReadPixels returns.
            if (_hostW != width || _hostH != height)
            {
                _host.Size = new Size(width, height);
                // Force the HWND to the new size even though never shown.
                _host.CreateControl();
                _hostW = width;
                _hostH = height;
            }

            glViewport(0, 0, width, height);
            glClearColor(0f, 0f, 0f, 0f);
            glClear(GL_COLOR_BUFFER_BIT);
            glDisable(GL_DEPTH_TEST);
            glEnable(GL_BLEND);
            glBlendFunc(GL_ONE, GL_ONE_MINUS_SRC_ALPHA);

            _glUseProgram(_program);
            _glUniform2f(_uViewport, (float)width, (float)height);

            uint[] vbA = new uint[1];
            _glGenBuffers(1, vbA);
            _glBindBuffer(GL_ARRAY_BUFFER, vbA[0]);
            _glBufferDataF(GL_ARRAY_BUFFER, new IntPtr(floatCount * 4), verts, GL_STATIC_DRAW);

            uint[] ibA = new uint[1];
            _glGenBuffers(1, ibA);
            _glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, ibA[0]);
            _glBufferDataI(GL_ELEMENT_ARRAY_BUFFER, new IntPtr(indexCount * 4), indices, GL_STATIC_DRAW);

            const int stride = 8 * 4;
            _glEnableVertexAttribArray((uint)_aPos);
            _glVertexAttribPointer((uint)_aPos, 2, GL_FLOAT, false, stride, IntPtr.Zero);
            _glEnableVertexAttribArray((uint)_aUV);
            _glVertexAttribPointer((uint)_aUV, 2, GL_FLOAT, false, stride, new IntPtr(8));
            _glEnableVertexAttribArray((uint)_aCol);
            _glVertexAttribPointer((uint)_aCol, 4, GL_FLOAT, false, stride, new IntPtr(16));

            _glDrawElements(GL_TRIANGLES, indexCount, GL_UNSIGNED_INT, IntPtr.Zero);

            _glDisableVertexAttribArray((uint)_aPos);
            _glDisableVertexAttribArray((uint)_aUV);
            _glDisableVertexAttribArray((uint)_aCol);
            _glBindBuffer(GL_ARRAY_BUFFER, 0);
            _glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, 0);
            _glDeleteBuffers(1, vbA);
            _glDeleteBuffers(1, ibA);
            _glUseProgram(0);

            byte[] px = new byte[width * height * 4];
            glReadPixels(0, 0, width, height, GL_RGBA, GL_UNSIGNED_BYTE, px);

            // RGBA (premultiplied) -> BGRA straight-alpha bitmap.
            var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, width, height);
            var bd = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int dstride = Math.Abs(bd.Stride);
                var out_ = new byte[dstride * height];
                for (int y = 0; y < height; y++)
                {
                    int srow = y * width * 4;
                    int drow = y * dstride;
                    for (int x = 0; x < width; x++)
                    {
                        int s = srow + x * 4;
                        int d = drow + x * 4;
                        int a = px[s + 3];
                        if (a == 0)
                        {
                            out_[d] = out_[d + 1] = out_[d + 2] = out_[d + 3] = 0;
                        }
                        else if (a == 255)
                        {
                            out_[d] = px[s + 2]; out_[d + 1] = px[s + 1];
                            out_[d + 2] = px[s]; out_[d + 3] = 255;
                        }
                        else
                        {
                            float inv = 255f / a;
                            int b = (int)(px[s + 2] * inv + 0.5f);
                            int g = (int)(px[s + 1] * inv + 0.5f);
                            int r = (int)(px[s] * inv + 0.5f);
                            out_[d] = (byte)Math.Min(255, b);
                            out_[d + 1] = (byte)Math.Min(255, g);
                            out_[d + 2] = (byte)Math.Min(255, r);
                            out_[d + 3] = (byte)a;
                        }
                    }
                }
                Marshal.Copy(out_, 0, bd.Scan0, out_.Length);
            }
            finally { bmp.UnlockBits(bd); }
            return bmp;
        }
    }
}
