﻿/*
 * The MIT License (MIT)
 *
 * Copyright (c) 2021 Mariusz Komorowski (komorra)
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), 
 * to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, 
 * and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES 
 * OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE 
 * OR OTHER DEALINGS IN THE SOFTWARE.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Data;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using System.Xml;

using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;

namespace NodeEditor
{
    /// <summary>
    /// Main control of Node Editor Winforms
    /// </summary>
    [ToolboxBitmap(typeof(NodesControl), "nodeed")]
    public partial class NodesControl : GLControl, IZoomable
    {
        internal class NodeToken
        {
            public MethodInfo Method;
            public NodeAttribute Attribute;
        }

        public NodesGraph graph = new NodesGraph();
        public bool needRepaint = true;
        private bool mdown = false;
        private bool ctrldown = false;
        private bool shiftdown = false;
        private PointF lastmpos;
        private SocketVisual dragSocket;
        private NodeVisual dragSocketNode;
        private PointF dragConnectionBegin;
        private PointF dragConnectionEnd;
        private Stack<NodeVisual> executionStack = new Stack<NodeVisual>();
        private bool rebuildConnectionDictionary = true;
        private Dictionary<string, NodeConnection> connectionDictionary = new Dictionary<string, NodeConnection>();

        /// <summary>
        /// Context of the editor. You should set here an instance that implements INodesContext interface.
        /// In context you should define your nodes (methods decorated by Node attribute).
        /// </summary>
        public INodesContext Context
        {
            get { return context; }
            set
            {
                if (context != null)
                {
                    context.FeedbackInfo -= ContextOnFeedbackInfo;
                }
                context = value;
                if (context != null)
                {
                    context.FeedbackInfo += ContextOnFeedbackInfo;
                }
            }
        }


        private float zoom = 1f;

        /// <summary>
        /// Indicates scale factor of visual appearance of node graph
        /// </summary>
        public float Zoom
        {
            get { return zoom; }
            set 
            { 
                zoom = value;
                PassZoomToNodes();
                Invalidate();
            }
        }

        private DrawInfo customDrawInfo = new DrawInfo();

        public DrawInfo CustomDrawInfo
        {
            get { return customDrawInfo; }
            set { customDrawInfo = value; }
        }


        /// <summary>
        /// If true, drawing events will use fast painting modes instead of high quality ones
        /// </summary>
        public bool PreferFastRendering { get; set; }

        /// <summary>
        /// Occurs when user selects a node. In the object will be passed node settings for unplugged inputs/outputs.
        /// </summary>
        public event Action<object> OnNodeContextSelected = delegate { };

        /// <summary>
        /// Occurs when node would to share its description.
        /// </summary>
        public event Action<string> OnNodeHint = delegate { };

        /// <summary>
        /// Indicates which part of control should be actually visible. It is useful when dragging nodes out of autoscroll parent control,
        /// to guarantee that moving node/connection is visible to user.
        /// </summary>
        public event Action<RectangleF> OnShowLocation = delegate { };

        /// <summary>
        /// Use this event to paint custom background - scaling is already applied into Graphics context.
        /// </summary>
        public event EventHandler<PaintEventArgs> OnPaintNodesBackground = delegate { };

        private readonly Dictionary<ToolStripMenuItem,int> allContextItems = new Dictionary<ToolStripMenuItem, int>();

        private PointF lastMouseLocation;

        private Point autoScroll;

        private PointF selectionStart;

        private PointF selectionEnd;

        private PointF dragStart = Point.Empty;

        private Matrix4 transform = Matrix4.Identity;

        private INodesContext context;

        private bool breakExecution = false;        

        private GLGraphics GLGraphics = new GLGraphics();

        /// <summary>
        /// Default constructor
        /// </summary>
        public NodesControl()
        {
            InitializeComponent();
            KeyDown += OnKeyDown;
            KeyUp += OnKeyUp;
            SetStyle(ControlStyles.Selectable, true);
        }

        private void ContextOnFeedbackInfo(string message, NodeVisual nodeVisual, FeedbackType type, object tag, bool breakExecution)
        {
            this.breakExecution = breakExecution;
            if (breakExecution)
            {
                nodeVisual.Feedback = type;
                OnNodeHint(message);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 7)
            {                
                return;                
            }
            base.WndProc(ref m);
        }

        private void OnKeyDown(object sender, KeyEventArgs keyEventArgs)
        {
            if (keyEventArgs.KeyCode == Keys.Delete)
            {
                DeleteSelectedNodes();
                DeleteHoveredConns();
            }

            if (keyEventArgs.Control)
                ctrldown = true;

            if (keyEventArgs.Shift)
                shiftdown = true;
        }

        private void OnKeyUp(object sender, KeyEventArgs keyEventArgs)
        {
            if (!keyEventArgs.Control)
                ctrldown = false;

            if (!keyEventArgs.Shift)
                shiftdown = false;
        }

        private void NodesControl_Resize(object sender, EventArgs e)
        {
            MakeCurrent();

            if (ClientSize.Height == 0)
                ClientSize = new System.Drawing.Size(ClientSize.Width, 1);

            GL.Viewport(0, 0, ClientSize.Width, ClientSize.Height);

            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadIdentity();
            GL.Ortho(0, ClientSize.Width, ClientSize.Height, 0, 0, 1);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            MakeCurrent();

            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Enable(EnableCap.Blend);

            GL.ClearColor(Color4.MidnightBlue);

            GL.Clear(ClearBufferMask.ColorBufferBit);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var sw = new Stopwatch();
            sw.Start();
            MakeCurrent();


            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Enable(EnableCap.Blend);

            GL.ClearColor(Color4.MidnightBlue);

            GL.Clear(ClearBufferMask.ColorBufferBit);


            //var g = e.Graphics;
            var g = GLGraphics;
            var clipBounds = e.Graphics.ClipBounds;

            GL.MatrixMode(MatrixMode.Projection);
            GL.PushMatrix();
            GL.MultMatrix(ref transform);

            //g.SmoothingMode = PreferFastRendering ? SmoothingMode.HighSpeed : SmoothingMode.HighQuality;
            //g.InterpolationMode = PreferFastRendering ? InterpolationMode.Low : InterpolationMode.HighQualityBilinear;
            //g.ScaleTransform(zoom, zoom);

            graph.Draw(g, clipBounds, GetLocationWithZoom(PointToClient(MousePosition)), MouseButtons, PreferFastRendering, customDrawInfo);

            if (dragSocket != null)
            {
                var pen = customDrawInfo.GetConnectionStyle(dragSocket.Type, true);
                NodesGraph.DrawConnection(g, clipBounds, pen, dragConnectionBegin, dragConnectionEnd, lastmpos, null, PreferFastRendering);
            }

            if (selectionStart != PointF.Empty)
            {
                var rect = Rectangle.Round(MakeRect(selectionStart, selectionEnd));
                g.FillRectangle(new SolidBrush(Color.FromArgb(50, Color.CornflowerBlue)), rect);
                g.DrawRectangle(new Pen(Color.DodgerBlue), rect);
            }

            needRepaint = false;

            GL.PopMatrix();

            SwapBuffers();

            sw.Stop();
            Console.WriteLine($"paint took {sw.ElapsedMilliseconds}ms");
        }

        private static RectangleF MakeRect(PointF a, PointF b)
        {
            var x1 = a.X;
            var x2 = b.X;
            var y1 = a.Y;
            var y2 = b.Y;
            return new RectangleF(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));
        }

        private void NodesControl_MouseMove(object sender, MouseEventArgs e)
        {
            var loc = GetLocationWithZoom(e.Location);

            if (dragStart != PointF.Empty)
            {
                transform = Matrix4.CreateTranslation(loc.X - dragStart.X, loc.Y - dragStart.Y, 0) * transform;

                loc = GetLocationWithZoom(e.Location); // update loc again since transform was updated
                dragStart = loc;
            }
            if (selectionStart != PointF.Empty)
            {
                selectionEnd = loc;
            }
            if (mdown)
            {                                            
                foreach (var node in graph.Nodes.Where(x => x.IsSelected))
                {
                    node.X += loc.X - lastmpos.X;
                    node.Y += loc.Y - lastmpos.Y;
                    node.DiscardCache();
                    node.LayoutEditor(zoom);
                }
                if (graph.Nodes.Exists(x => x.IsSelected))
                {
                    var n = graph.Nodes.FirstOrDefault(x => x.IsSelected);
                    var bound = new RectangleF(new PointF(n.X,n.Y), n.GetNodeBounds());
                    foreach (var node in graph.Nodes.Where(x=>x.IsSelected))
                    {
                        bound = RectangleF.Union(bound, new RectangleF(new PointF(node.X, node.Y), node.GetNodeBounds()));
                    }
                    OnShowLocation(bound);
                }
                Invalidate();
                
                if (dragSocket != null)
                {
                    var center = new PointF(dragSocket.X + dragSocket.Width/2f, dragSocket.Y + dragSocket.Height/2f);
                    if (dragSocket.Input)
                    {
                        dragConnectionBegin.X += loc.X - lastmpos.X;
                        dragConnectionBegin.Y += loc.Y - lastmpos.Y;
                        dragConnectionEnd = center;
                        OnShowLocation(new RectangleF(dragConnectionBegin, new SizeF(10, 10)));
                    }
                    else
                    {
                        dragConnectionBegin = center;
                        dragConnectionEnd.X += loc.X - lastmpos.X;
                        dragConnectionEnd.Y += loc.Y - lastmpos.Y;
                        OnShowLocation(new RectangleF(dragConnectionEnd, new SizeF(10, 10)));
                    }
                    
                }
                lastmpos = loc;
            }            

            Invalidate();
        }

        private void NodesControl_MouseDown(object sender, MouseEventArgs e)
        {
            var loc = GetLocationWithZoom(e.Location);

            if (e.Button == MouseButtons.Right)
            {
                dragStart = loc;

                Focus();
            }

            if (e.Button == MouseButtons.Left)
            {
                selectionStart  = PointF.Empty;                

                Focus();

                if ((ModifierKeys & Keys.Shift) != Keys.Shift)
                {
                    graph.Nodes.ForEach(x => x.IsSelected = false);
                }

                var node = graph.Nodes.OrderBy(x => x.Order).FirstOrDefault(x => new RectangleF(new PointF(x.X, x.Y), x.GetHeaderSize()).Contains(loc));

                if (node != null && !mdown)
                {
                    node.IsSelected = true;
                    
                    node.Order = graph.Nodes.Min(x => x.Order) - 1;
                    if (node.CustomEditor != null)
                    {
                        node.CustomEditor.BringToFront();
                        PassZoomToNodeCustomEditor(node.CustomEditor);
                    }
                    mdown = true;
                    lastmpos = loc;

                    Refresh();
                }
                if (node == null && !mdown)
                {
                    var nodeWhole =
                    graph.Nodes.OrderBy(x => x.Order).FirstOrDefault(
                        x => new RectangleF(new PointF(x.X, x.Y), x.GetNodeBounds()).Contains(loc));
                    if (nodeWhole != null)
                    {
                        node = nodeWhole;
                        var socket = nodeWhole.GetSockets().FirstOrDefault(x => x.GetBounds().Contains(loc));
                        if (socket != null)
                        {
                            if ((ModifierKeys & Keys.Control) == Keys.Control)
                            {
                                var connection =
                                    graph.Connections.FirstOrDefault(
                                        x => x.InputNode == nodeWhole && x.InputSocketName == socket.Name);

                                if (connection != null)
                                {
                                    dragSocket =
                                        connection.OutputNode.GetSockets()
                                            .FirstOrDefault(x => x.Name == connection.OutputSocketName);
                                    dragSocketNode = connection.OutputNode;
                                }
                                else
                                {
                                    connection =
                                        graph.Connections.FirstOrDefault(
                                            x => x.OutputNode == nodeWhole && x.OutputSocketName == socket.Name);

                                    if (connection != null)
                                    {
                                        dragSocket =
                                            connection.InputNode.GetSockets()
                                                .FirstOrDefault(x => x.Name == connection.InputSocketName);
                                        dragSocketNode = connection.InputNode;
                                    }
                                }

                                graph.Connections.Remove(connection);
                                rebuildConnectionDictionary = true;
                            }
                            else
                            {
                                dragSocket = socket;
                                dragSocketNode = nodeWhole;
                            }
                            dragConnectionBegin = loc;
                            dragConnectionEnd = loc;
                            mdown = true;
                            lastmpos = loc;
                        }
                    }
                    else
                    {
                        selectionStart = selectionEnd = loc;
                    }
                }
                if (node != null)
                {
                    OnNodeContextSelected(node.GetNodeContext());
                }
            }

            Invalidate();
        }

        private void PassZoomToNodeCustomEditor(Control control)
        {
            var zoomable = control as IZoomable;
            if (zoomable != null)
            {
                zoomable.Zoom = zoom * zoom;
            }
        }

        private bool IsConnectable(SocketVisual a, SocketVisual b)
        {
            var input = a.Input ? a : b;
            var output = a.Input ? b : a;
            var otype = Type.GetType(output.Type.FullName.Replace("&", ""), AssemblyResolver, TypeResolver);
            var itype = Type.GetType(input.Type.FullName.Replace("&", ""), AssemblyResolver, TypeResolver);
            if (otype == null || itype == null) return false;
            var allow = otype == itype || otype.IsSubclassOf(itype);
            return allow;
        }

        private Type TypeResolver(Assembly assembly, string name, bool inh)
        {
            if (assembly == null) assembly = ResolveAssembly(name);
            if (assembly == null) return null;
            return assembly.GetType(name);
        }

        private Assembly ResolveAssembly(string fullTypeName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(x => x.GetTypes().Any(o => o.FullName == fullTypeName));
        }

        private Assembly AssemblyResolver(AssemblyName assemblyName)
        {
            return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(x => x.FullName == assemblyName.FullName);
        }

        private void NodesControl_MouseUp(object sender, MouseEventArgs e)
        {
            var loc = GetLocationWithZoom(e.Location);

            if (e.Button == MouseButtons.Right)
            {
                dragStart = PointF.Empty;
            }

            if (selectionStart != PointF.Empty)
            {
                var rect = MakeRect(selectionStart, selectionEnd);
                graph.Nodes.ForEach(
                    x => x.IsSelected = rect.Contains(new RectangleF(new PointF(x.X, x.Y), x.GetNodeBounds())));
                selectionStart = PointF.Empty;
            }

            if (dragSocket != null)
            {
                var nodeWhole =
                    graph.Nodes.OrderBy(x => x.Order).FirstOrDefault(
                        x => new RectangleF(new PointF(x.X, x.Y), x.GetNodeBounds()).Contains(loc));
                if (nodeWhole != null)
                {
                    var socket = nodeWhole.GetSockets().FirstOrDefault(x => x.GetBounds().Contains(loc));
                    if (socket != null)
                    {
                        if (IsConnectable(dragSocket,socket) && dragSocket.Input != socket.Input)
                        {                                                        
                            var nc = new NodeConnection();
                            if (!dragSocket.Input)
                            {
                                nc.OutputNode = dragSocketNode;
                                nc.OutputSocketName = dragSocket.Name;
                                nc.InputNode = nodeWhole;
                                nc.InputSocketName = socket.Name;
                            }
                            else
                            {
                                nc.InputNode = dragSocketNode;
                                nc.InputSocketName = dragSocket.Name;
                                nc.OutputNode = nodeWhole;
                                nc.OutputSocketName = socket.Name;
                            }

                            graph.Connections.RemoveAll(
                                x => x.InputNode == nc.InputNode && x.InputSocketName == nc.InputSocketName);

                            graph.Connections.Add(nc);
                            rebuildConnectionDictionary = true;
                        }
                    }
                }
            }
           
            dragSocket = null;
            mdown = false;
            Invalidate();
        }

        private void NodesControl_MouseWheel(object sender, MouseEventArgs e)
        {
            if (shiftdown)
            {
                ((HandledMouseEventArgs)e).Handled = true;
                int scrollAmount = e.Delta * SystemInformation.MouseWheelScrollLines / 120;

                int newValue = HorizontalScroll.Value - scrollAmount;

                int minValue = HorizontalScroll.Minimum;
                int maxValue = HorizontalScroll.Maximum - HorizontalScroll.LargeChange + 1;
                newValue = Math.Max(minValue, Math.Min(newValue, maxValue));

                HorizontalScroll.Value = newValue;
            }

            if (ctrldown)
            {
                ((HandledMouseEventArgs)e).Handled = true;
                var loc = GetLocationWithZoom(e.Location);

                var amount = 1.3f;
                var zoom = e.Delta > 0 ? amount : 1 / amount;

                var t = Matrix4.Identity;
                t *= Matrix4.CreateTranslation(-loc.X, -loc.Y, 0);
                t *= Matrix4.CreateScale(zoom, zoom, 1);
                t *= Matrix4.CreateTranslation(loc.X, loc.Y, 0);

                transform = t * transform;

                Invalidate();
            }
        }

        private void AddToMenu(ToolStripItemCollection items, NodeToken token, string path, EventHandler click)
        {
            var pathParts = path.Split(new[] {'/'}, StringSplitOptions.RemoveEmptyEntries);
            var first = pathParts.FirstOrDefault();
            ToolStripMenuItem item = null;
            if (!items.ContainsKey(first))
            {
                item = new ToolStripMenuItem(first);
                item.Name = first;                
                item.Tag = token;
                items.Add(item);
            }
            else
            {
                item = items[first] as ToolStripMenuItem;
            }
            var next = string.Join("/", pathParts.Skip(1));
            if (!string.IsNullOrEmpty(next))
            {
                item.MouseEnter += (sender, args) => OnNodeHint("");
                AddToMenu(item.DropDownItems, token, next, click);
            }
            else
            {
                item.Click += click;
                item.Click += (sender, args) =>
                {
                    var i = allContextItems.Keys.FirstOrDefault(x => x.Name == item.Name);
                    allContextItems[i]++;
                };
                item.MouseEnter += (sender, args) => OnNodeHint(token.Attribute.Description ?? "");
                if (!allContextItems.Keys.Any(x => x.Name == item.Name))
                {
                    allContextItems.Add(item, 0);
                }
            }
        }

        private void NodesControl_MouseClick(object sender, MouseEventArgs e)
        {
            var loc = GetLocationWithZoom(e.Location);
            lastMouseLocation = loc;

            if (Context == null) return;

            if (e.Button == MouseButtons.Right)
            {
                var methods = Context.GetType().GetMethods();
                var nodes =
                    methods.Select(
                        x =>
                            new
                                NodeToken()
                            {
                                Method = x,
                                Attribute =
                                    x.GetCustomAttributes(typeof (NodeAttribute), false)
                                        .Cast<NodeAttribute>()
                                        .FirstOrDefault()
                            }).Where(x => x.Attribute != null);

                var context = new ContextMenuStrip();
                if (graph.Nodes.Exists(x=>x.IsSelected))
                {
                    context.Items.Add("Delete Node(s)", null, ((o, args) =>
                    {
                        DeleteSelectedNodes();
                    }));
                    context.Items.Add("Duplicate Node(s)", null, ((o, args) =>
                    {
                        DuplicateSelectedNodes();
                    }));
                    context.Items.Add("Change Color ...", null, ((o, args) =>
                    {
                        ChangeSelectedNodesColor();
                    }));
                    if(graph.Nodes.Count(x=>x.IsSelected)==2)
                    {
                        var sel = graph.Nodes.Where(x => x.IsSelected).ToArray();
                        context.Items.Add("Check Impact", null, ((o,args)=>
                        {
                            if(HasImpact(sel[0],sel[1]) || HasImpact(sel[1],sel[0]))
                            {
                                MessageBox.Show("One node has impact on other.", "Impact detected.", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }
                            else
                            {
                                MessageBox.Show("These nodes not impacts themselves.", "No impact.", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }
                        }));                       
                    }
                    context.Items.Add(new ToolStripSeparator());
                }
                if (graph.Connections.Any(x => x.IsHover))
                {
                    context.Items.Add("Delete Connection(s)", null, ((o, args) =>
                    {
                        DeleteHoveredConns();
                    }));
                    context.Items.Add(new ToolStripSeparator());
                }
                if (allContextItems.Values.Any(x => x > 0))
                {
                    var handy = allContextItems.Where(x => x.Value > 0 && !string.IsNullOrEmpty(((x.Key.Tag) as NodeToken).Attribute.Menu)).OrderByDescending(x => x.Value).Take(8);
                    foreach (var kv in handy)
                    {
                        context.Items.Add(kv.Key);
                    }
                    context.Items.Add(new ToolStripSeparator());
                }
                foreach (var node in nodes.OrderBy(x=>x.Attribute.Path))
                {
                    AddToMenu(context.Items, node, node.Attribute.Path, (s,ev) =>
                    {
                        var tag = (s as ToolStripMenuItem).Tag as NodeToken;

                        var nv = new NodeVisual();
                        nv.X = lastMouseLocation.X;
                        nv.Y = lastMouseLocation.Y;
                        nv.Type = new MethodNodeType() { Method = node.Method };
                        nv.Callable = node.Attribute.IsCallable;
                        nv.Name = node.Attribute.Name;
                        nv.Order = graph.Nodes.Count;
                        nv.ExecInit = node.Attribute.IsExecutionInitiator;
                        nv.XmlExportName = node.Attribute.XmlExportName;
                        nv.CustomWidth = node.Attribute.Width;
                        nv.CustomHeight = node.Attribute.Height;

                        if (node.Attribute.CustomEditor != null)
                        {
                            Control ctrl = null;
                            nv.CustomEditor = ctrl = Activator.CreateInstance(node.Attribute.CustomEditor) as Control;
                            if (ctrl != null)
                            {
                                ctrl.Tag = nv;                                
                                Controls.Add(ctrl);
                                PassZoomToNodeCustomEditor(ctrl);
                            }
                            nv.LayoutEditor(zoom);
                        }

                        graph.Nodes.Add(nv);
                        Refresh();
                        needRepaint = true;
                    });                    
                }
                context.Show(MousePosition);
            }
        }

        private PointF GetLocationWithZoom(PointF location)
        {
            var vec = new Vector4(location.X, location.Y, 0, 1);
            vec *= transform.Inverted();
            return new PointF(vec.X, vec.Y);
        }

        private void PassZoomToNodes()
        {
            foreach(var node in graph.Nodes)
            {
                if(node.CustomEditor != null)
                {
                    PassZoomToNodeCustomEditor(node.CustomEditor);
                    node.DiscardCache();
                    node.LayoutEditor(zoom);
                }
            }
        }

        private void ChangeSelectedNodesColor()
        {
            ColorDialog cd = new ColorDialog();
            cd.FullOpen = true;
            if (cd.ShowDialog() == DialogResult.OK)
            {
                foreach (var n in graph.Nodes.Where(x => x.IsSelected))
                {
                    n.NodeColor = cd.Color;
                }
            }
            Refresh();
            needRepaint = true;
        }

        private void DuplicateSelectedNodes()
        {
            var cloned = new List<NodeVisual>();
            foreach (var n in graph.Nodes.Where(x => x.IsSelected))
            {
                int count = graph.Nodes.Count(x => x.IsSelected);
                var ms = new MemoryStream();
                var bw = new BinaryWriter(ms);
                SerializeNode(bw, n);
                ms.Seek(0, SeekOrigin.Begin);
                var br = new BinaryReader(ms);
                var clone = DeserializeNode(br);
                clone.X += 40;
                clone.Y += 40;
                clone.GUID = Guid.NewGuid().ToString();
                cloned.Add(clone);
                br.Dispose();
                bw.Dispose();
                ms.Dispose();
            }
            graph.Nodes.ForEach(x => x.IsSelected = false);
            cloned.ForEach(x => x.IsSelected = true);
            cloned.Where(x => x.CustomEditor != null).ToList().ForEach(x =>
            {
                x.CustomEditor.BringToFront();
                PassZoomToNodeCustomEditor(x.CustomEditor);
            });
            graph.Nodes.AddRange(cloned);
            Invalidate();
        }

        private void DeleteSelectedNodes()
        {
            if (graph.Nodes.Exists(x => x.IsSelected))
            {
                foreach (var n in graph.Nodes.Where(x => x.IsSelected))
                {
                    Controls.Remove(n.CustomEditor);
                    graph.Connections.RemoveAll(
                        x => x.OutputNode == n || x.InputNode == n);
                }
                graph.Nodes.RemoveAll(x => graph.Nodes.Where(n => n.IsSelected).Contains(x));
            }
            Invalidate();
        }

        /// <summary>
        /// Executes whole node graph (when called parameterless) or given node when specified.
        /// </summary>
        /// <param name="node"></param>
        public void Execute(NodeVisual node = null)
        {            
            var nodeQueue = new Queue<NodeVisual>();
            nodeQueue.Enqueue(node);

            while (nodeQueue.Count > 0)
            {
                //Refresh();
                if (breakExecution)
                {
                    breakExecution = false;
                    executionStack.Clear();
                    return;
                }

                var init = nodeQueue.Dequeue() ?? graph.Nodes.FirstOrDefault(x => x.ExecInit);
                if (init != null)
                {
                    init.Feedback = FeedbackType.Debug;

                    Resolve(init);
                    init.Execute(Context);
                    
                    var connection =
                        graph.Connections.FirstOrDefault(
                            x => x.OutputNode == init && x.IsExecution && x.OutputSocket.Value != null && (x.OutputSocket.Value as ExecutionPath).IsSignaled);
                    if (connection == null)
                    {
                        connection = graph.Connections.FirstOrDefault(x => x.OutputNode == init && x.IsExecution && x.OutputSocket.IsMainExecution);
                    }
                    else
                    {
                        executionStack.Push(init);
                    }
                    if (connection != null)
                    {
                        connection.InputNode.IsBackExecuted = false;
                        //Execute(connection.InputNode);
                        nodeQueue.Enqueue(connection.InputNode);
                    }
                    else
                    {
                        if (executionStack.Count > 0)
                        {
                            var back = executionStack.Pop();
                            back.IsBackExecuted = true;
                            Execute(back);
                        }
                    }
                }
            }
        }

        public List<NodeVisual> GetNodes(params string[] nodeNames)
        {
            var nodes = graph.Nodes.Where(x => nodeNames.Contains(x.Name));
            return nodes.ToList();
        }

        public bool HasImpact(NodeVisual startNode, NodeVisual endNode)
        {
            var connections = graph.Connections.Where(x => x.OutputNode == startNode && !x.IsExecution);
            foreach (var connection in connections)
            {
                if(connection.InputNode == endNode)
                {
                    return true;
                }
                bool nextImpact = HasImpact(connection.InputNode, endNode);
                if(nextImpact)
                {
                    return true;
                }
            }

            return false;
        }

        public void ExecuteResolving(params string[] nodeNames)
        {
            var nodes = graph.Nodes.Where(x => nodeNames.Contains(x.Name));

            foreach (var node in nodes)
            {
                ExecuteResolvingInternal(node);
            }
        }

        private void ExecuteResolvingInternal(NodeVisual node)
        {
            var icontext = (node.GetNodeContext() as DynamicNodeContext);
            foreach (var input in node.GetInputs())
            {
                var connection =
                    graph.Connections.FirstOrDefault(x => x.InputNode == node && x.InputSocketName == input.Name);
                if (connection != null)
                {
                    Resolve(connection.OutputNode);
                    
                    connection.OutputNode.Execute(Context);

                    ExecuteResolvingInternal(connection.OutputNode);
                    
                    var ocontext = (connection.OutputNode.GetNodeContext() as DynamicNodeContext);
                    icontext[connection.InputSocketName] = ocontext[connection.OutputSocketName];
                }
            }
        }

        /// <summary>
        /// Resolves given node, resolving it all dependencies recursively.
        /// </summary>
        /// <param name="node"></param>
        private void Resolve(NodeVisual node)
        {
            var icontext = (node.GetNodeContext() as DynamicNodeContext);
            foreach (var input in node.GetInputs())
            {
                var connection = GetConnection(node.GUID + input.Name);
                    //graph.Connections.FirstOrDefault(x => x.InputNode == node && x.InputSocketName == input.Name);
                if (connection != null)
                {
                    Resolve(connection.OutputNode);
                    if (!connection.OutputNode.Callable)
                    {                        
                        connection.OutputNode.Execute(Context);
                    }
                    var ocontext = (connection.OutputNode.GetNodeContext() as DynamicNodeContext);
                    icontext[connection.InputSocketName] = ocontext[connection.OutputSocketName];                    
                }
            }
        }

        private NodeConnection GetConnection(string v)
        {
            if(rebuildConnectionDictionary)
            {
                rebuildConnectionDictionary = false;
                connectionDictionary.Clear();
                foreach (var conn in graph.Connections)
                {
                    connectionDictionary.Add(conn.InputNode.GUID + conn.InputSocketName, conn);
                }
            }
            NodeConnection nc = null;
            if (connectionDictionary.TryGetValue(v, out nc))
            {
                return nc;
            }
            return null;
        }

        public string ExportToXml()
        {
            var xml = new XmlDocument();

            XmlElement el = (XmlElement)xml.AppendChild(xml.CreateElement("NodeGrap"));
            el.SetAttribute("Created", DateTime.Now.ToString());
            var nodes = el.AppendChild(xml.CreateElement("Nodes"));
            foreach (var node in graph.Nodes)
            {
                var xmlNode = (XmlElement)nodes.AppendChild(xml.CreateElement("Node"));
                xmlNode.SetAttribute("Name", node.XmlExportName);
                xmlNode.SetAttribute("Id", node.GetGuid());
                var xmlContext = (XmlElement)xmlNode.AppendChild(xml.CreateElement("Context"));
                var context = node.GetNodeContext() as DynamicNodeContext;
                foreach (var kv in context)
                {
                    var ce = (XmlElement)xmlContext.AppendChild(xml.CreateElement("ContextMember"));
                    ce.SetAttribute("Name", kv);
                    ce.SetAttribute("Value", Convert.ToString(context[kv] ?? ""));
                    ce.SetAttribute("Type", context[kv] == null ? "" : context[kv].GetType().FullName);
                }
            }
            var connections = el.AppendChild(xml.CreateElement("Connections"));
            foreach (var conn in graph.Connections)
            {
                var xmlConn = (XmlElement)nodes.AppendChild(xml.CreateElement("Connection"));
                xmlConn.SetAttribute("OutputNodeId", conn.OutputNode.GetGuid());
                xmlConn.SetAttribute("OutputNodeSocket", conn.OutputSocketName);
                xmlConn.SetAttribute("InputNodeId", conn.InputNode.GetGuid());
                xmlConn.SetAttribute("InputNodeSocket", conn.InputSocketName);
            }
            StringBuilder sb = new StringBuilder();
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                NewLineChars = "\r\n",
                NewLineHandling = NewLineHandling.Replace
            };
            using (XmlWriter writer = XmlWriter.Create(sb, settings))
            {
                xml.Save(writer);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Serializes current node graph to binary data.
        /// </summary>        
        public byte[] Serialize()
        {
            using (var bw = new BinaryWriter(new MemoryStream()))
            {
                bw.Write("NodeSystemP"); //recognization string
                bw.Write(1000); //version
                bw.Write(graph.Nodes.Count);
                foreach (var node in graph.Nodes)
                {
                    SerializeNode(bw, node);
                }
                bw.Write(graph.Connections.Count);
                foreach (var connection in graph.Connections)
                {
                    bw.Write(connection.OutputNode.GUID);
                    bw.Write(connection.OutputSocketName);

                    bw.Write(connection.InputNode.GUID);
                    bw.Write(connection.InputSocketName);
                    bw.Write(0); //additional data size per connection
                }
                bw.Write(0); //additional data size per graph
                return (bw.BaseStream as MemoryStream).ToArray();
            }
        }
        
        private static void SerializeNode(BinaryWriter bw, NodeVisual node)
        {
            bw.Write(node.GUID);
            bw.Write(node.X);
            bw.Write(node.Y);
            bw.Write(node.Callable);
            bw.Write(node.ExecInit);
            bw.Write(node.Name);
            bw.Write(node.Order);
            if (node.CustomEditor == null)
            {
                bw.Write("");
                bw.Write("");
            }
            else
            {
                bw.Write(node.CustomEditor.GetType().Assembly.GetName().Name);
                bw.Write(node.CustomEditor.GetType().FullName);
            }
            bw.Write(node.Type.Name);
            var context = (node.GetNodeContext() as DynamicNodeContext).Serialize();
            bw.Write(context.Length);
            bw.Write(context);
            bw.Write(8); //additional data size per node
            bw.Write(node.Int32Tag);
            bw.Write(node.NodeColor.ToArgb());
        }

        /// <summary>
        /// Restores node graph state from previously serialized binary data.
        /// </summary>
        /// <param name="data"></param>
        public void Deserialize(byte[] data)
        {
            using (var br = new BinaryReader(new MemoryStream(data)))
            {
                var ident = br.ReadString();
                if (ident != "NodeSystemP") return;
                rebuildConnectionDictionary = true;
                graph.Connections.Clear();
                graph.Nodes.Clear();
                Controls.Clear();

                var version = br.ReadInt32();
                int nodeCount = br.ReadInt32();
                for (int i = 0; i < nodeCount; i++)
                {
                    var nv = DeserializeNode(br);

                    graph.Nodes.Add(nv);
                }
                var connectionsCount = br.ReadInt32();
                for (int i = 0; i < connectionsCount; i++)
                {
                    var con = new NodeConnection();
                    var og = br.ReadString();
                    con.OutputNode = graph.Nodes.FirstOrDefault(x => x.GUID == og);
                    con.OutputSocketName = br.ReadString();
                    var ig = br.ReadString();
                    con.InputNode = graph.Nodes.FirstOrDefault(x => x.GUID == ig);
                    con.InputSocketName = br.ReadString();
                    br.ReadBytes(br.ReadInt32()); //read additional data

                    graph.Connections.Add(con);
                    rebuildConnectionDictionary = true;
                }
                br.ReadBytes(br.ReadInt32()); //read additional data
            }
            Refresh();
        }

        private NodeVisual DeserializeNode(BinaryReader br)
        {
            var nv = new NodeVisual();
            nv.GUID = br.ReadString();
            nv.X = br.ReadSingle();
            nv.Y = br.ReadSingle();
            nv.Callable = br.ReadBoolean();
            nv.ExecInit = br.ReadBoolean();
            nv.Name = br.ReadString();
            nv.Order = br.ReadInt32();
            var customEditorAssembly = br.ReadString();
            var customEditor = br.ReadString();
            var method = Context.GetType().GetMethod(br.ReadString());
            nv.Type = new MethodNodeType { Method = method };

            var attribute = method.GetCustomAttributes(typeof(NodeAttribute), false)
                                        .Cast<NodeAttribute>()
                                        .FirstOrDefault();
            if(attribute!=null)
            {
                nv.CustomWidth = attribute.Width;
                nv.CustomHeight = attribute.Height;
            }
            (nv.GetNodeContext() as DynamicNodeContext).Deserialize(br.ReadBytes(br.ReadInt32()));
            var additional = br.ReadInt32(); //read additional data
            if (additional >= 4)
            {
                nv.Int32Tag = br.ReadInt32();
                if(additional >= 8)
                {
                    nv.NodeColor = Color.FromArgb(br.ReadInt32());
                }
            }
            if (additional > 8)
            {
                br.ReadBytes(additional - 8);
            }

            if (customEditor != "")
            {
                nv.CustomEditor =
                    Activator.CreateInstance(AppDomain.CurrentDomain, customEditorAssembly, customEditor).Unwrap() as Control;

                Control ctrl = nv.CustomEditor;
                if (ctrl != null)
                {
                    ctrl.Tag = nv;
                    Controls.Add(ctrl);
                    PassZoomToNodeCustomEditor(ctrl);
                }
                nv.LayoutEditor(zoom);
            }
            return nv;
        }

        public void AddNode(NodeVisual nv, bool repaint = true)
        {
            nv.Order = graph.Nodes.Count;
            if (nv.Type.CustomEditor != null)
            {
                nv.CustomEditor = Activator.CreateInstance(nv.Type.CustomEditor) as Control;

                Control ctrl = nv.CustomEditor;
                if (ctrl != null)
                {
                    ctrl.Tag = nv;
                    Controls.Add(ctrl);
                    PassZoomToNodeCustomEditor(ctrl);
                }
                nv.LayoutEditor(zoom);
            }
            graph.Nodes.Add(nv);
            if (repaint)
            {
                Refresh();
                needRepaint = true;
            }
        }

        /// <summary>
        /// Clears node graph state.
        /// </summary>
        public void Clear()
        {
            transform = Matrix4.Identity;
            graph.Nodes.Clear();
            graph.Connections.Clear();
            Controls.Clear();
            Refresh();
            rebuildConnectionDictionary = true;
        }

        private void DeleteHoveredConns()
        {
            foreach (NodeConnection e in graph.Connections.Where(x => x.IsHover).ToArray())
            {
                graph.Connections.Remove(e);
            }

            Invalidate();
        }
    }
}
