﻿using SharpDX.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace D3D12HelloDepthBuffer
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            var form = new RenderForm("Hello Depth Buffer");
            form.Width = 1280;
            form.Height = 800;
            form.Show();

            using (HelloDepthBuffer app = new HelloDepthBuffer())
            {
                app.Initialize(form);

                using (var loop = new RenderLoop(form))
                {
                    while (loop.NextFrame())
                    {
                        app.Update();
                        app.Render();
                    }
                }
            }
        }
    }
}
