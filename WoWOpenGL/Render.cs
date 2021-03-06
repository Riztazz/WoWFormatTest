﻿using OpenTK;
using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using WoWFormatLib.FileReaders;
using OpenTK.Input;
using System.Timers;
using WoWOpenGL.Loaders;
using System.ComponentModel;

namespace WoWOpenGL
{
    public class Render : GameWindow
    {
        OldCamera ActiveCamera;
       
        private GLControl glControl;
        private bool gLoaded = false;

        private Material[] materials;
        private RenderBatch[] renderbatches;
        private uint[] VBOid;

        private static bool isWMO = false;

        private static float angle = 90.0f;
        private static float dragX;
        private static float dragY;
        private static float dragZ;
        private static float zoom;

        private bool modelLoaded = false;

        private BackgroundWorker worker;

        private CacheStorage cache = new CacheStorage();

        public Render(string ModelPath, BackgroundWorker worker = null)
        {
            dragX = 0.0f;
            dragY = 0.0f;
            dragZ = -7.5f;

            if(worker == null)
            {
                Console.WriteLine("Didn't get a backgroundworker, creating one!");
                this.worker = new BackgroundWorker();
            }
            else
            {
                this.worker = worker;
            }

            System.Windows.Forms.Integration.WindowsFormsHost wfc = MainWindow.winFormControl;

            ActiveCamera = new OldCamera((int)wfc.ActualWidth, (int)wfc.ActualHeight);
            ActiveCamera.Pos = new Vector3(10.0f, -10.0f, -7.5f);

            if (ModelPath.EndsWith(".m2", StringComparison.OrdinalIgnoreCase))
            {
                modelLoaded = true;
                LoadM2(ModelPath);
            }
            else if (ModelPath.EndsWith(".wmo", StringComparison.OrdinalIgnoreCase))
            {
                modelLoaded = true;
                LoadWMO(ModelPath);
            }
            else
            {
                modelLoaded = false;
            }

            glControl = new GLControl(new OpenTK.Graphics.GraphicsMode(32, 24, 0, 8), 3, 0, OpenTK.Graphics.GraphicsContextFlags.Default);
            glControl.Width = (int)wfc.ActualWidth;
            glControl.Height = (int)wfc.ActualHeight;
            glControl.Left = 0;
            glControl.Top = 0;
            glControl.Load += glControl_Load;
            glControl.Paint += RenderFrame;
            glControl.Resize += glControl_Resize;
            glControl_Resize(glControl, EventArgs.Empty);
            glControl.MakeCurrent();

            wfc.Child = glControl;
        }

        public void DrawAxes()
        {
            GL.Begin(PrimitiveType.Lines);

            GL.Color3(Color.DarkRed);  // x axis
            GL.Vertex3(-10, 0, 0);
            GL.Vertex3(10, 0, 0);

            GL.Color3(Color.ForestGreen);  // y axis    
            GL.Vertex3(0, -10, 0);
            GL.Vertex3(0, 10, 0);

            GL.Color3(Color.LightBlue);  // z axis
            GL.Vertex3(0, 0, -10);
            GL.Vertex3(0, 0, 10);

            GL.End();
        }

        private void glControl_Load(object sender, EventArgs e)
        {

            Console.WriteLine("Loading GLcontrol..");
            glControl.MakeCurrent();
            GL.Enable(EnableCap.Texture2D);
            GL.Enable(EnableCap.DepthTest);
            
            GL.ClearColor(OpenTK.Graphics.Color4.Black);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            
            InitializeInputTick();
            ActiveCamera.setupGLRenderMatrix();
            Console.WriteLine("GLcontrol is done loading!");

        }

        private void glControl_Resize(object sender, EventArgs e)
        {
        }

        private void LoadM2(string modelpath)
        {
            if (!WoWFormatLib.Utils.CASC.FileExists(modelpath))
            {
                throw new Exception("Model does not exist!");
            }

            worker.ReportProgress(0, "Loading model..");

            M2Reader reader = new M2Reader();

            string filename = modelpath;

            reader.LoadM2(filename);
            VBOid = new uint[2];
            GL.GenBuffers(2, VBOid);

            GL.BindBuffer(BufferTarget.ArrayBuffer, VBOid[0]);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, VBOid[1]);

            GL.EnableClientState(ArrayCap.VertexArray);
            GL.EnableClientState(ArrayCap.NormalArray);

            worker.ReportProgress(20, "Reading model indices..");

            List<uint> indicelist = new List<uint>();
            for (int i = 0; i < reader.model.skins[0].triangles.Count(); i++)
            {
                indicelist.Add(reader.model.skins[0].triangles[i].pt1);
                indicelist.Add(reader.model.skins[0].triangles[i].pt2);
                indicelist.Add(reader.model.skins[0].triangles[i].pt3);
            }

            uint[] indices = indicelist.ToArray();

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, VBOid[1]);
            GL.BufferData(BufferTarget.ElementArrayBuffer, (IntPtr)(indices.Length * sizeof(uint)), indices, BufferUsageHint.StaticDraw);

            worker.ReportProgress(30, "Reading model vertices..");

            Vertex[] vertices = new Vertex[reader.model.vertices.Count()];

            for (int i = 0; i < reader.model.vertices.Count(); i++)
            {
                vertices[i].Position = new Vector3(reader.model.vertices[i].position.X, reader.model.vertices[i].position.Z, reader.model.vertices[i].position.Y * -1);
                vertices[i].Normal = new Vector3(reader.model.vertices[i].normal.X, reader.model.vertices[i].normal.Z, reader.model.vertices[i].normal.Y);
                vertices[i].TexCoord = new Vector2(reader.model.vertices[i].textureCoordX, reader.model.vertices[i].textureCoordY);
            }
            GL.BindBuffer(BufferTarget.ArrayBuffer, VBOid[0]);
            GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(vertices.Length * 8 * sizeof(float)), vertices, BufferUsageHint.StaticDraw);

            GL.Enable(EnableCap.Texture2D);

            worker.ReportProgress(40, "Loading textures..");

            materials = new Material[reader.model.textures.Count()];
            for (int i = 0; i < reader.model.textures.Count(); i++)
            {
                Console.WriteLine("Loading texture " + i);
                string texturefilename = @"dungeons\textures\testing\color_13.blp";
                materials[i].flags = reader.model.textures[i].flags;
                Console.WriteLine("      Requires type " + reader.model.textures[i].type + " texture");
                switch (reader.model.textures[i].type)
                {
                    case 0:
                        Console.WriteLine("      Texture given in file!");
                        texturefilename = reader.model.textures[i].filename;
                        break;
                    case 1:
                        string[] csfilenames = WoWFormatLib.DBC.DBCHelper.getTexturesByModelFilename(filename, (int)reader.model.textures[i].type, i);
                        if(csfilenames.Count() > 0){
                            texturefilename = csfilenames[0];
                        }
                        else
                        {
                            Console.WriteLine("      No type 1 texture found, falling back to placeholder texture");
                        }
                        break;
                    case 2:
                        if (WoWFormatLib.Utils.CASC.FileExists(Path.ChangeExtension(modelpath, ".blp")))
                        {
                            Console.WriteLine("      BLP exists!");
                            texturefilename = Path.ChangeExtension(modelpath, ".blp");
                        }
                        else
                        {
                            Console.WriteLine("      Type 2 does not exist!");
                            //needs lookup?
                        }
                        break;
                    case 11:
                        string[] cdifilenames = WoWFormatLib.DBC.DBCHelper.getTexturesByModelFilename(filename, (int)reader.model.textures[i].type);
                        for (int ti = 0; ti < cdifilenames.Count(); ti++)
                        {
                            if (WoWFormatLib.Utils.CASC.FileExists(modelpath.Replace(reader.model.name + ".M2", cdifilenames[ti] + ".blp")))
                            {
                                texturefilename = modelpath.Replace(reader.model.name + ".M2", cdifilenames[ti] + ".blp");
                            }
                        }
                        break;
                    default:
                        Console.WriteLine("      Falling back to placeholder texture");
                        break;
                }

                Console.WriteLine("      Eventual filename is " + texturefilename);
                materials[i].textureID = BLPLoader.LoadTexture(texturefilename, cache);
                materials[i].filename = texturefilename;
            }

            worker.ReportProgress(60, "Loading renderbatches..");
            renderbatches = new RenderBatch[reader.model.skins[0].submeshes.Count()];
            for (int i = 0; i < reader.model.skins[0].submeshes.Count(); i++)
            {
                if(filename.StartsWith("character", StringComparison.CurrentCultureIgnoreCase)){
                    if (reader.model.skins[0].submeshes[i].submeshID != 0)
                    {
                        if (!reader.model.skins[0].submeshes[i].submeshID.ToString().EndsWith("01"))
                        {
                            continue;
                        }
                    }

                    Console.WriteLine("Loading submesh " + reader.model.skins[0].submeshes[i].submeshID + "("+ reader.model.skins[0].submeshes[i].unk2 + ")");
                }
                
                renderbatches[i].firstFace = reader.model.skins[0].submeshes[i].startTriangle;
                renderbatches[i].numFaces = reader.model.skins[0].submeshes[i].nTriangles;
                for (int tu = 0; tu < reader.model.skins[0].textureunit.Count(); tu++)
                {
                    if (reader.model.skins[0].textureunit[tu].submeshIndex == i)
                    {
                        renderbatches[i].blendType = reader.model.renderflags[reader.model.skins[0].textureunit[tu].renderFlags].blendingMode;
                        renderbatches[i].materialID = reader.model.texlookup[reader.model.skins[0].textureunit[tu].texture].textureID;
                    }
                }
            }

            worker.ReportProgress(100, "Done.");

            gLoaded = true;
        }

        private void LoadWMO(string modelpath)
        {
            Console.WriteLine("Loading WMO file..");
            WMOReader reader = new WMOReader();
            string filename = modelpath;
            //Load WMO
            reader.LoadWMO(filename);

            //Enable Vertex Arrays
            GL.EnableClientState(ArrayCap.VertexArray);
            //Enable Normal Arrays
            GL.EnableClientState(ArrayCap.NormalArray);
            //Enable TexCoord arrays
            GL.EnableClientState(ArrayCap.TextureCoordArray);
           
            //Set up buffer IDs
            VBOid = new uint[(reader.wmofile.group.Count() * 2) + 2];
            GL.GenBuffers((reader.wmofile.group.Count() * 2) + 2, VBOid);

            for (int i = 0; i < reader.wmofile.doodadNames.Count(); i++)
            {
                //Console.WriteLine(reader.wmofile.doodadNames[i].filename);
            }

            for (int g = 0; g < reader.wmofile.group.Count(); g++)
            {
                if (reader.wmofile.group[g].mogp.vertices == null) { continue; }
                //Switch to Vertex buffer
                GL.BindBuffer(BufferTarget.ArrayBuffer, VBOid[g * 2]);

                Vertex[] vertices = new Vertex[reader.wmofile.group[g].mogp.vertices.Count()];

                for (int i = 0; i < reader.wmofile.group[g].mogp.vertices.Count(); i++)
                {
                    vertices[i].Position = new Vector3(reader.wmofile.group[g].mogp.vertices[i].vector.X, reader.wmofile.group[g].mogp.vertices[i].vector.Z, reader.wmofile.group[g].mogp.vertices[i].vector.Y);
                    vertices[i].Normal = new Vector3(reader.wmofile.group[g].mogp.normals[i].normal.X, reader.wmofile.group[g].mogp.normals[i].normal.Z, reader.wmofile.group[g].mogp.normals[i].normal.Y);
                    vertices[i].TexCoord = new Vector2(reader.wmofile.group[g].mogp.textureCoords[0][i].X, reader.wmofile.group[g].mogp.textureCoords[0][i].Y);
                }


                //Push to buffer
                GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(vertices.Length * 8 * sizeof(float)), vertices, BufferUsageHint.StaticDraw);

                //Switch to Index buffer
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, VBOid[(g * 2) + 1]);

                List<uint> indicelist = new List<uint>();
                for (int i = 0; i < reader.wmofile.group[g].mogp.indices.Count(); i++)
                {
                    indicelist.Add(reader.wmofile.group[g].mogp.indices[i].indice);
                }

                uint[] indices = indicelist.ToArray();

                //Push to buffer
                GL.BufferData(BufferTarget.ElementArrayBuffer, (IntPtr)(indices.Length * sizeof(uint)), indices, BufferUsageHint.StaticDraw);
            }

            GL.Enable(EnableCap.Texture2D);

            materials = new Material[reader.wmofile.materials.Count()];
            for (int i = 0; i < reader.wmofile.materials.Count(); i++)
            {
                for (int ti = 0; ti < reader.wmofile.textures.Count(); ti++)
                {
                    
                    if (reader.wmofile.textures[ti].startOffset == reader.wmofile.materials[i].texture1)
                    {
                        materials[i].textureID = BLPLoader.LoadTexture(reader.wmofile.textures[ti].filename, cache);
                        materials[i].filename = reader.wmofile.textures[ti].filename;
                    }
                }
            }

            int numRenderbatches = 0;
            //Get total amount of render batches
            for (int i = 0; i < reader.wmofile.group.Count(); i++)
            {
                if (reader.wmofile.group[i].mogp.renderBatches == null) { continue; }
                numRenderbatches = numRenderbatches + reader.wmofile.group[i].mogp.renderBatches.Count();
            }

            renderbatches = new RenderBatch[numRenderbatches];

            int rb = 0;
            for (int g = 0; g < reader.wmofile.group.Count(); g++)
            {
                var group = reader.wmofile.group[g];
                if (group.mogp.renderBatches == null) { continue; }
                for (int i = 0; i < group.mogp.renderBatches.Count(); i++)
                {
                    var batch = group.mogp.renderBatches[i];
                    renderbatches[rb].firstFace = batch.firstFace;
                    renderbatches[rb].numFaces = batch.numFaces;
                    if (batch.flags == 2)
                    {
                        renderbatches[rb].materialID = (uint) group.mogp.renderBatches[i].possibleBox2_3;
                    }
                    else
                    {
                        renderbatches[rb].materialID = group.mogp.renderBatches[i].materialID;
                    }
                    renderbatches[rb].blendType = reader.wmofile.materials[group.mogp.renderBatches[i].materialID].blendMode;
                    renderbatches[rb].groupID = (uint)g;
                    rb++;
                }
            }

            Console.WriteLine("  " + reader.wmofile.group.Count() + " skins");
            Console.WriteLine("  " + materials.Count() + " materials");
            Console.WriteLine("  " + renderbatches.Count() + " renderbatches");
            Console.WriteLine("  " + reader.wmofile.group[0].mogp.vertices.Count() + " vertices");
            Console.WriteLine("Done loading WMO file!");
            
            gLoaded = true;
            isWMO = true;
        }

        private static void InitializeInputTick()
        {
            var timer = new Timer();
            timer.Enabled = true;
            timer.Interval = 1000 / 60;
            timer.Elapsed += new ElapsedEventHandler(InputTick);
            timer.Start();
        }

        private static void InputTick(object sender, EventArgs e)
        {
            float speed = 0.01f * (float) ControlsWindow.camSpeed;

            OpenTK.Input.MouseState mouseState = OpenTK.Input.Mouse.GetState();
            OpenTK.Input.KeyboardState keyboardState = OpenTK.Input.Keyboard.GetState();

            if (keyboardState.IsKeyDown(Key.Up))
            {
                dragY = dragY + speed;
            }

            if (keyboardState.IsKeyDown(Key.Down))
            {
                dragY = dragY - speed;
            }

            if (keyboardState.IsKeyDown(Key.Left))
            {
                angle = angle + speed;
            }

            if (keyboardState.IsKeyDown(Key.Right))
            {
                angle = angle - speed;
            }

            if (keyboardState.IsKeyDown(Key.Z))
            {
                dragZ = dragZ - speed;
            }

            if (keyboardState.IsKeyDown(Key.X))
            {
                dragZ = dragZ + speed;
            }

            if (keyboardState.IsKeyDown(Key.Q))
            {
                angle = angle + 0.5f;
            }

            if (keyboardState.IsKeyDown(Key.E))
            {
                angle = angle - 0.5f;
            }

            //if (mouseInRender)
            //{
            //dragZ = (mouseState.WheelPrecise / speed) - (7.5f); //Startzoom is at -7.5f 
            //}

        }

        private void RenderFrame(object sender, EventArgs e) //This is called every frame
        {
            if (!gLoaded) { return; }
            glControl.MakeCurrent();

            ActiveCamera.Pos = new Vector3(dragX, dragY, dragZ);
            ActiveCamera.setupGLRenderMatrix();

            if (!gLoaded) return;

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            
            GL.Enable(EnableCap.Texture2D);
            GL.EnableClientState(ArrayCap.VertexArray);
            GL.EnableClientState(ArrayCap.NormalArray);
            GL.EnableClientState(ArrayCap.TextureCoordArray);
            GL.Rotate(angle, 0.0, 1.0, 0.0);

            for (int i = 0; i < renderbatches.Count(); i++)
            {
                if (!isWMO)
                {
                    GL.BindBuffer(BufferTarget.ArrayBuffer, VBOid[0]);
                    GL.VertexPointer(3, VertexPointerType.Float, 32, 0);
                    GL.NormalPointer(NormalPointerType.Float, 32, 12);
                    GL.TexCoordPointer(2, TexCoordPointerType.Float, 32, 24);
                    GL.BindBuffer(BufferTarget.ElementArrayBuffer, VBOid[1]);
                }
                else
                {
                    //Console.WriteLine("Switching to buffer " + renderbatches[i].groupID * 2);
                    GL.BindBuffer(BufferTarget.ArrayBuffer, VBOid[renderbatches[i].groupID * 2]);
                    GL.VertexPointer(3, VertexPointerType.Float, 32, 0);
                    GL.NormalPointer(NormalPointerType.Float, 32, 12);
                    GL.TexCoordPointer(2, TexCoordPointerType.Float, 32, 24);
                    GL.BindBuffer(BufferTarget.ElementArrayBuffer, VBOid[(renderbatches[i].groupID * 2) + 1]);
                }
                
                if (renderbatches[i].materialID > materials.Count() - 1) //temp hackfix
                {
                    Console.WriteLine("[ERROR] Material ID encountered which is lower than material count!!!");
                    //continue;
                }
                else
                {
                    switch(renderbatches[i].blendType)
                    {
                        case 0: //Combiners_Opaque (Blend disabled)
                            GL.Disable(EnableCap.Blend);
                            break;
                        case 1: //Combiners_Mod (Blend enabled, Src = ONE, Dest = ZERO, SrcAlpha = ONE, DestAlpha = ZERO)
                            GL.Enable(EnableCap.Blend);
                            //Not BlendingFactorSrc.One and BlendingFactorDest.Zero!
                            //GL.BlendFuncSeparate(BlendingFactorSrc.One, BlendingFactorDest.Zero, BlendingFactorSrc.One, BlendingFactorDest.Zero);
                            GL.BlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha);
                            break;
                        case 2: //Combiners_Decal (Blend enabled, Src = SRC_ALPHA, Dest = INV_SRC_ALPHA, SrcAlpha = SRC_ALPHA, DestAlpha = INV_SRC_ALPHA )
                            GL.Enable(EnableCap.Blend);
                            GL.BlendFuncSeparate(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha, BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha);
                            //Tried:
                            //BlendingFactorSrc.SrcAlpha, BlendingFactorDest.DstAlpha
                            //BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha
                            //BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusDstAlpha
                            break;
                        case 3: //Combiners_Add (Blend enabled, Src = SRC_COLOR, Dest = DEST_COLOR, SrcAlpha = SRC_ALPHA, DestAlpha = DEST_ALPHA )
                            GL.Enable(EnableCap.Blend);
                            GL.BlendFunc(BlendingFactorSrc.SrcColor, BlendingFactorDest.DstColor);
                            break;
                        case 4: //Combiners_Mod2x (Blend enabled, Src = SRC_ALPHA, Dest = ONE, SrcAlpha = SRC_ALPHA, DestAlpha = ONE )
                            GL.Enable(EnableCap.Blend);
                            GL.BlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.One);
                            break;
                        case 5: //Combiners_Fade (Blend enabled, Src = SRC_ALPHA, Dest = INV_SRC_ALPHA, SrcAlpha = SRC_ALPHA, DestAlpha = INV_SRC_ALPHA )
                            GL.Enable(EnableCap.Blend);
                            GL.BlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha);
                            break; 
                        case 6: //Used in the Deeprun Tram subway glass, supposedly (Blend enabled, Src = DEST_COLOR, Dest = SRC_COLOR, SrcAlpha = DEST_ALPHA, DestAlpha = SRC_ALPHA )
                            GL.Enable(EnableCap.Blend);
                            GL.BlendFunc(BlendingFactorSrc.DstColor, BlendingFactorDest.SrcColor);
                            break;
                        case 7: //World\Expansion05\Doodads\Shadowmoon\Doodads\6FX_Fire_Grassline_Doodad_blue_LARGE.m2
                            break;
                        default:
                            throw new Exception("Unknown blend type " + renderbatches[i].blendType);
                    }
                    GL.BindTexture(TextureTarget.Texture2D, materials[renderbatches[i].materialID].textureID);
                }
                
                GL.DrawRangeElements(PrimitiveType.Triangles, renderbatches[i].firstFace, (renderbatches[i].firstFace + renderbatches[i].numFaces), (int)renderbatches[i].numFaces, DrawElementsType.UnsignedInt, new IntPtr(renderbatches[i].firstFace * 4));
                if (GL.GetError().ToString() != "NoError")
                {
                    Console.WriteLine(GL.GetError().ToString());
                }
            }

           // DrawAxes();
            glControl.SwapBuffers();
            glControl.Invalidate();
        }

        public struct RenderBatch
        {
            public uint firstFace;
            public uint materialID;
            public uint numFaces;
            public uint groupID;
            public uint blendType;
        }

        private struct Vertex
        {
            public Vector3 Position;
            public Vector3 Normal;
            public Vector2 TexCoord;
        }

        public struct Material
        {
            public string filename;
            public WoWFormatLib.Structs.M2.TextureFlags flags;
            public int textureID;
        }
    }
}