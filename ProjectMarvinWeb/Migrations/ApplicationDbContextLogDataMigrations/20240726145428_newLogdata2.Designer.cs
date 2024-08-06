﻿// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ProjectMarvin.Data;

#nullable disable

namespace ProjectMarvin.Migrations.ApplicationDbContextLogDataMigrations
{
    [DbContext(typeof(ApplicationDbContextLogData))]
    [Migration("20240726145428_newLogdata2")]
    partial class newLogdata2
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "8.0.7");

            modelBuilder.Entity("LogEntry", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("IPAdress")
                        .HasColumnType("TEXT");

                    b.Property<DateTime?>("LogDate")
                        .HasColumnType("TEXT");

                    b.Property<string>("LogType")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Message")
                        .HasColumnType("TEXT");

                    b.Property<string>("Sender")
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("LogEntries");
                });
#pragma warning restore 612, 618
        }
    }
}
